[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$PackageName = "com.candy.survivalcraftAPI1_9.autotest",
    [switch]$NoContentRepack
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$defaultOutput = Join-Path $artifactsRoot "android-device-autotest"
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $defaultOutput
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$workRoot = Join-Path $outputRoot "work"
$generatedManifest = Join-Path $workRoot "AndroidManifest.AutoTest.xml"
$metadataPath = Join-Path $outputRoot "android-device-autotest.json"
$artifactPath = Join-Path $outputRoot "Survivalcraft-Android-AutoTest-arm64.apk"

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

function Invoke-DotNet {
    param([string[]]$Arguments)
    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

if (-not (Test-PathInside $outputRoot $artifactsRoot)) {
    throw "OutputDirectory must be below '$artifactsRoot'."
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

Push-Location $repositoryRoot
try {
    if (!$NoContentRepack) {
        Invoke-DotNet @(
            "msbuild",
            (Join-Path $repositoryRoot "Survivalcraft.Windows\Survivalcraft.Windows.csproj"),
            "-t:PreBuild",
            "-p:Configuration=Release",
            "-p:SkipContentZip=false",
            "-p:SkipNetworkModPackage=true",
            "--nologo")
    }

    $testModProject = Join-Path $repositoryRoot "Survivalcraft.AndroidTestAutomation\Survivalcraft.AndroidTestAutomation.csproj"
    Invoke-DotNet @(
        "build", $testModProject,
        "-c", "Release",
        "-p:GenerateModPackage=true",
        "--nologo")
    $testModPackage = Get-ChildItem (
        Join-Path $repositoryRoot "Survivalcraft.AndroidTestAutomation\bin\Release") `
        -Filter "Survivalcraft.AndroidTestAutomation.scmod" -File -Recurse |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $testModPackage) {
        throw "Android device test automation mod was not produced."
    }

    $multiplayerProject = Join-Path $repositoryRoot "Survivalcraft.Multiplayer\Survivalcraft.Multiplayer.csproj"
    Invoke-DotNet @(
        "build", $multiplayerProject,
        "-c", "Release",
        "-p:GenerateModPackage=true",
        "--nologo")
    $multiplayerModPackage = Get-ChildItem (
        Join-Path $repositoryRoot "Survivalcraft.Multiplayer\bin\Release") `
        -Filter "Survivalcraft.Multiplayer.scmod" -File -Recurse |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $multiplayerModPackage) {
        throw "Multiplayer mod package was not produced."
    }
    $androidInternalMods = Join-Path $repositoryRoot "Survivalcraft.Android\obj\Release\net10.0-android\InternalMods"
    New-Item -ItemType Directory -Path $androidInternalMods -Force | Out-Null
    $stagedMultiplayerMod = Join-Path $androidInternalMods "Survivalcraft.Multiplayer.scmod"
    Copy-Item -LiteralPath $multiplayerModPackage.FullName -Destination $stagedMultiplayerMod -Force

    [xml]$manifest = Get-Content -LiteralPath (
        Join-Path $repositoryRoot "Survivalcraft.Android\Properties\AndroidManifest.xml") -Raw
    $manifest.manifest.package = $PackageName
    $xmlSettings = [System.Xml.XmlWriterSettings]::new()
    $xmlSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $xmlSettings.Indent = $true
    $xmlWriter = [System.Xml.XmlWriter]::Create($generatedManifest, $xmlSettings)
    try {
        $manifest.Save($xmlWriter)
    }
    finally {
        $xmlWriter.Dispose()
    }

    $buildStartedUtc = [DateTime]::UtcNow.AddSeconds(-2)
    $androidProjectDirectory = Join-Path $repositoryRoot "Survivalcraft.Android"
    $manifestProjectRelativePath = [System.IO.Path]::GetRelativePath(
        $androidProjectDirectory,
        $generatedManifest)
    Invoke-DotNet @(
        "publish",
        (Join-Path $repositoryRoot "Survivalcraft.Android\Survivalcraft.Android.csproj"),
        "-c", "Release",
        "-f", "net10.0-android",
        "-r", "android-arm64",
        "--nologo",
        "-p:SkipNetworkModPackage=true",
        "-p:NetworkModDestinationFile=$stagedMultiplayerMod",
        "-p:AndroidPackageFormat=apk",
        "-p:EnableAndroidDeviceTestAutomation=true",
        "-p:AndroidDeviceTestModPackage=$($testModPackage.FullName)",
        "-p:AndroidManifest=$manifestProjectRelativePath")

    $signedApk = Get-ChildItem (
        Join-Path $repositoryRoot "Survivalcraft.Android\bin\Release") `
        -Filter "*-Signed.apk" -File -Recurse |
        Where-Object { $_.LastWriteTimeUtc -ge $buildStartedUtc } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $signedApk) {
        throw "A newly signed Android test APK was not produced."
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($signedApk.FullName)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        $requiredAssets = @(
            "assets/InternalMods/Survivalcraft.Multiplayer.scmod",
            "assets/InternalMods/Survivalcraft.AndroidTestAutomation.scmod")
        foreach ($requiredAsset in $requiredAssets) {
            if ($entryNames -notcontains $requiredAsset) {
                throw "Android test APK is missing required asset '$requiredAsset'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Copy-Item -LiteralPath $signedApk.FullName -Destination $artifactPath -Force
    $metadata = [ordered]@{
        schemaVersion = 1
        packageName = $PackageName
        dataDirectory = "/sdcard/Survivalcraft2.4_API1.9_AutoTest"
        runtimeIdentifier = "android-arm64"
        abi = "arm64-v8a"
        apk = $artifactPath
        apkSha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        testMod = $testModPackage.FullName
        builtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        sourceCommit = (& git rev-parse HEAD).Trim()
        sourceDirty = @(& git status --porcelain --untracked-files=no).Count -gt 0
    }
    [System.IO.File]::WriteAllText(
        $metadataPath,
        ($metadata | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "ANDROID DEVICE TEST BUILD PASSED"
    Write-Host "apk=$artifactPath"
    Write-Host "metadata=$metadataPath"
}
finally {
    Pop-Location
}
