[CmdletBinding()]
param(
    [string]$ReleaseDirectory,
    [switch]$StaticOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseSpec = Get-Content (Join-Path $repositoryRoot "release\multiplayer-preview.json") -Raw |
    ConvertFrom-Json
$releaseRoot = Join-Path $repositoryRoot "artifacts\releases"
$releasePath = if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    Join-Path $releaseRoot $releaseSpec.tag
}
else {
    [System.IO.Path]::GetFullPath($ReleaseDirectory)
}
$verificationRootBase = Join-Path $repositoryRoot "TestResults\release-verification"
$verificationRoot = Join-Path $verificationRootBase $releaseSpec.tag

function Test-PathInside {
    param([string]$Candidate, [string]$Parent)
    $candidatePath = [System.IO.Path]::GetFullPath($Candidate).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    return $candidatePath.StartsWith(
        $parentPath + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Reset-SafeDirectory {
    param([string]$Path, [string]$AllowedRoot)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-PathInside $fullPath $AllowedRoot)) {
        throw "Refusing to reset directory outside '$AllowedRoot': $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

function Assert-Sha256Sums {
    param([string]$Directory, [string]$ChecksumFile)
    $lines = Get-Content -LiteralPath $ChecksumFile |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($lines.Count -eq 0) {
        throw "Checksum file is empty: $ChecksumFile"
    }
    foreach ($line in $lines) {
        if ($line -notmatch '^([0-9a-fA-F]{64}) \*(.+)$') {
            throw "Invalid SHA256SUMS line: $line"
        }
        $expected = $Matches[1].ToLowerInvariant()
        $name = $Matches[2]
        $path = Join-Path $Directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Checksummed artifact is missing: $path"
        }
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            throw "SHA-256 mismatch for '$name'. Expected $expected, actual $actual."
        }
    }
}

function Get-AndroidBuildTool {
    param([string]$Name)
    $androidSdk = if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
        $env:ANDROID_HOME
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) {
        $env:ANDROID_SDK_ROOT
    }
    else {
        throw "ANDROID_HOME or ANDROID_SDK_ROOT is required for APK verification."
    }
    $tool = Get-ChildItem (Join-Path $androidSdk "build-tools") -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName $Name } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($tool)) {
        throw "Android build tool '$Name' was not found under '$androidSdk'."
    }
    return $tool
}

function Assert-AndroidApk {
    param([string]$ApkPath)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ApkPath)
    try {
        $nativeAbis = @($archive.Entries |
            Where-Object { $_.FullName -match '^lib/([^/]+)/' } |
            ForEach-Object { [regex]::Match($_.FullName, '^lib/([^/]+)/').Groups[1].Value } |
            Sort-Object -Unique)
        if ($nativeAbis.Count -ne 1 -or $nativeAbis[0] -ne $releaseSpec.android.abi) {
            throw "APK native ABIs are '$($nativeAbis -join ',')'; expected only '$($releaseSpec.android.abi)'."
        }
        $automationLeak = @($archive.Entries | Where-Object {
            $_.FullName -match "(?i)TestAutomation"
        })
        if ($automationLeak.Count -gt 0) {
            throw "Public APK contains test automation assets: $($automationLeak.FullName -join ', ')"
        }
        $modEntry = $archive.Entries | Where-Object {
            $_.FullName.Equals(
                "assets/InternalMods/Survivalcraft.Multiplayer.scmod",
                [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($null -eq $modEntry) {
            throw "APK does not contain assets/InternalMods/Survivalcraft.Multiplayer.scmod."
        }
        $memory = [System.IO.MemoryStream]::new()
        $entryStream = $modEntry.Open()
        try {
            $entryStream.CopyTo($memory)
        }
        finally {
            $entryStream.Dispose()
        }
        $memory.Position = 0
        $modArchive = [System.IO.Compression.ZipArchive]::new(
            $memory,
            [System.IO.Compression.ZipArchiveMode]::Read,
            $false)
        try {
            foreach ($requiredEntry in @("modinfo.json", "Survivalcraft.Multiplayer.dll")) {
                if (-not ($modArchive.Entries | Where-Object {
                    $_.FullName.Equals($requiredEntry, [System.StringComparison]::OrdinalIgnoreCase)
                })) {
                    throw "Embedded multiplayer scmod is missing '$requiredEntry'."
                }
            }
        }
        finally {
            $modArchive.Dispose()
            $memory.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $apksigner = Get-AndroidBuildTool "apksigner.bat"
    & $apksigner verify --verbose --print-certs $ApkPath
    if ($LASTEXITCODE -ne 0) {
        throw "apksigner rejected '$ApkPath'."
    }

    $aapt = Get-AndroidBuildTool "aapt.exe"
    $badging = (& $aapt dump badging $ApkPath) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        throw "aapt could not inspect '$ApkPath'."
    }
    $expectedVersionCode = [regex]::Escape([string]$releaseSpec.android.versionCode)
    $expectedVersionName = [regex]::Escape([string]$releaseSpec.android.versionName)
    if ($badging -notmatch "versionCode='$expectedVersionCode'") {
        throw "APK versionCode does not match '$($releaseSpec.android.versionCode)'."
    }
    if ($badging -notmatch "versionName='$expectedVersionName'") {
        throw "APK versionName does not match '$($releaseSpec.android.versionName)'."
    }
    if ($badging -notmatch "native-code:\s+'$([regex]::Escape([string]$releaseSpec.android.abi))'") {
        throw "aapt did not report the expected ARM64 native-code contract."
    }
}

if (-not (Test-Path -LiteralPath $releasePath -PathType Container)) {
    throw "Release directory does not exist: $releasePath"
}

Push-Location $repositoryRoot
try {
    $windowsArchive = Join-Path $releasePath $releaseSpec.artifacts.windowsX64
    $relayArchive = Join-Path $releasePath $releaseSpec.artifacts.relayWindowsX64
    $androidApk = Join-Path $releasePath $releaseSpec.artifacts.androidArm64
    $checksums = Join-Path $releasePath $releaseSpec.artifacts.checksums
    foreach ($requiredFile in @($windowsArchive, $relayArchive, $androidApk, $checksums)) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Release artifact is missing: $requiredFile"
        }
    }
    Assert-Sha256Sums $releasePath $checksums
    Assert-AndroidApk $androidApk

    Reset-SafeDirectory $verificationRoot $verificationRootBase
    $windowsRuntime = Join-Path $verificationRoot "windows-runtime"
    $relayRuntime = Join-Path $verificationRoot "relay-runtime"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($windowsArchive, $windowsRuntime)
    [System.IO.Compression.ZipFile]::ExtractToDirectory($relayArchive, $relayRuntime)

    $publicAutomationFiles = Get-ChildItem -LiteralPath $windowsRuntime -File -Recurse |
        Where-Object { $_.Name -like "*TestAutomation*" }
    if ($publicAutomationFiles) {
        throw "Public Windows archive leaked TestAutomation: $($publicAutomationFiles.FullName -join ', ')"
    }
    if (Test-Path -LiteralPath (Join-Path $windowsRuntime "Mods\Survivalcraft.Multiplayer.scmod")) {
        throw "Multiplayer scmod is duplicated in the public Mods directory."
    }

    if (-not $StaticOnly) {
        Invoke-DotNet @(
            "build", (Join-Path $repositoryRoot "Survivalcraft.TestAutomation\Survivalcraft.TestAutomation.csproj"),
            "-c", "Release", "--nologo",
            "-p:SkipNetworkModPackage=true", "-p:SkipContentZip=true")
        $automationScmod = Join-Path $repositoryRoot (
            "Survivalcraft.Test\bin\Release\net10.0-windows\win-x64\Mods\Survivalcraft.TestAutomation.scmod")
        if (-not (Test-Path -LiteralPath $automationScmod -PathType Leaf)) {
            throw "TestAutomation scmod was not produced: $automationScmod"
        }
        $testMods = Join-Path $windowsRuntime "Mods"
        New-Item -ItemType Directory -Path $testMods -Force | Out-Null
        Copy-Item -LiteralPath $automationScmod -Destination (
            Join-Path $testMods "Survivalcraft.TestAutomation.scmod")

        Invoke-DotNet @(
            "build", (Join-Path $repositoryRoot "Survivalcraft.TestHost\Survivalcraft.TestHost.csproj"),
            "-c", "Release", "--nologo")
        $hostDll = Join-Path $repositoryRoot (
            "Survivalcraft.TestHost\bin\Release\net10.0\Survivalcraft.TestHost.dll")
        Invoke-DotNet @(
            $hostDll, "doctor", "--runtime", $windowsRuntime,
            "--startup-timeout", "120")
        Invoke-DotNet @(
            $hostDll, "run", "core-multiplayer",
            "--runtime", $windowsRuntime,
            "--artifacts", (Join-Path $verificationRoot "direct-core"),
            "--seed", "20260809",
            "--step-timeout", "90",
            "--scenario-timeout", "480")
        Invoke-DotNet @(
            $hostDll, "run", "relay-mod-channel",
            "--runtime", $windowsRuntime,
            "--relay-runtime", $relayRuntime,
            "--artifacts", (Join-Path $verificationRoot "relay-mod-channel"),
            "--seed", "20260809",
            "--step-timeout", "90",
            "--scenario-timeout", "480")
    }

    $remainingGameProcesses = @(Get-Process -Name "Survivalcraft" -ErrorAction SilentlyContinue)
    if ($remainingGameProcesses.Count -gt 0) {
        throw "Survivalcraft process residue detected: $($remainingGameProcesses.Id -join ', ')"
    }
    $remainingRelayProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine.Contains("Survivalcraft.RelayServer.dll", [System.StringComparison]::OrdinalIgnoreCase) -and
            $_.CommandLine.Contains($relayRuntime, [System.StringComparison]::OrdinalIgnoreCase)
        })
    if ($remainingRelayProcesses.Count -gt 0) {
        throw "Relay Server process residue detected: $($remainingRelayProcesses.ProcessId -join ', ')"
    }

    $summary = [ordered]@{
        schemaVersion = 1
        tag = [string]$releaseSpec.tag
        verifiedAtUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        checksums = "passed"
        androidArm64 = "passed"
        publicPackageIsolation = "passed"
        hiddenWindowsDoctor = if ($StaticOnly) { "skipped" } else { "passed" }
        directCore = if ($StaticOnly) { "skipped" } else { "passed" }
        relayModChannel = if ($StaticOnly) { "skipped" } else { "passed" }
        processResidue = "none"
    }
    $summaryPath = Join-Path $verificationRoot "verification-summary.json"
    [System.IO.File]::WriteAllText(
        $summaryPath,
        ($summary | ConvertTo-Json -Depth 4) + [System.Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "RELEASE VERIFICATION PASSED"
    Write-Host "summary=$summaryPath"
}
finally {
    Pop-Location
}
