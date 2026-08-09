[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseSpecPath = Join-Path $repositoryRoot "release\multiplayer-preview.json"
$releaseSpec = Get-Content $releaseSpecPath -Raw | ConvertFrom-Json
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$releaseRoot = Join-Path $artifactsRoot "releases"
$workRoot = Join-Path $artifactsRoot "release-work"
$tagWorkRoot = Join-Path $workRoot $releaseSpec.tag
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $releaseRoot $releaseSpec.tag
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}

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

function New-DeterministicZip {
    param([string]$SourceDirectory, [string]$DestinationFile)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $source = [System.IO.Path]::GetFullPath($SourceDirectory)
    $destination = [System.IO.Path]::GetFullPath($DestinationFile)
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "ZIP source directory does not exist: $source"
    }
    $destinationParent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Force
    }
    $stream = [System.IO.File]::Open(
        $destination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $files = Get-ChildItem -LiteralPath $source -File -Recurse |
                Sort-Object { [System.IO.Path]::GetRelativePath($source, $_.FullName) }
            $fixedTimestamp = [System.DateTimeOffset]::Parse("2000-01-01T00:00:00Z")
            foreach ($file in $files) {
                $entryName = [System.IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $input = [System.IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

if (-not (Test-PathInside $outputRoot $releaseRoot)) {
    throw "OutputDirectory must be a child of '$releaseRoot'."
}

Push-Location $repositoryRoot
try {
    $trackedChanges = @(& git status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect git status."
    }
    $sourceDirty = $trackedChanges.Count -gt 0
    if ($sourceDirty -and -not $AllowDirty) {
        throw "Tracked files are dirty. Commit the release sources or rerun with -AllowDirty for local validation only."
    }

    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the source commit."
    }
    $sourceDate = (& git show -s --format=%cI HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the source commit date."
    }

    Reset-SafeDirectory $outputRoot $releaseRoot
    Reset-SafeDirectory $tagWorkRoot $workRoot

    $contentZip = Join-Path $repositoryRoot "Survivalcraft\Content.zip"
    New-DeterministicZip (Join-Path $repositoryRoot "Survivalcraft\Content") $contentZip

    $multiplayerProject = Join-Path $repositoryRoot "Survivalcraft.Multiplayer\Survivalcraft.Multiplayer.csproj"
    Invoke-DotNet @(
        "build", $multiplayerProject, "-c", "Release", "--nologo",
        "-p:GenerateModPackage=false",
        "-p:ContinuousIntegrationBuild=true")
    $multiplayerDll = Get-ChildItem (Join-Path $repositoryRoot "Survivalcraft.Multiplayer\bin\Release") `
        -Filter "Survivalcraft.Multiplayer.dll" -File -Recurse |
        Where-Object { $_.FullName -notmatch "[\\/]ref[\\/]" } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $multiplayerDll) {
        throw "Survivalcraft.Multiplayer.dll was not produced."
    }

    $scmodStage = Join-Path $tagWorkRoot "multiplayer-scmod"
    New-Item -ItemType Directory -Path $scmodStage -Force | Out-Null
    Copy-Item -LiteralPath $multiplayerDll.FullName -Destination $scmodStage
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "Survivalcraft.Multiplayer\modinfo.json") -Destination $scmodStage
    $multiplayerScmod = Join-Path $tagWorkRoot "Survivalcraft.Multiplayer.scmod"
    New-DeterministicZip $scmodStage $multiplayerScmod

    $windowsStage = Join-Path $tagWorkRoot "windows-x64"
    New-Item -ItemType Directory -Path $windowsStage -Force | Out-Null
    Invoke-DotNet @(
        "publish", (Join-Path $repositoryRoot "Survivalcraft.Windows\Survivalcraft.Windows.csproj"),
        "-c", "Release", "-r", "win-x64", "--self-contained", "false",
        "-o", $windowsStage, "--nologo",
        "-p:SkipNetworkModPackage=true", "-p:SkipContentZip=true",
        "-p:ContinuousIntegrationBuild=true")
    $internalMods = Join-Path $windowsStage "InternalMods"
    New-Item -ItemType Directory -Path $internalMods -Force | Out-Null
    Copy-Item -LiteralPath $multiplayerScmod -Destination (Join-Path $internalMods "Survivalcraft.Multiplayer.scmod")
    if (-not (Test-Path -LiteralPath (Join-Path $windowsStage "Content.zip"))) {
        Copy-Item -LiteralPath $contentZip -Destination (Join-Path $windowsStage "Content.zip")
    }
    $automationLeak = Get-ChildItem -LiteralPath $windowsStage -File -Recurse |
        Where-Object { $_.Name -like "*TestAutomation*" }
    if ($automationLeak) {
        throw "Public Windows package contains TestAutomation: $($automationLeak.FullName -join ', ')"
    }

    $relayStage = Join-Path $tagWorkRoot "relay-win-x64"
    New-Item -ItemType Directory -Path $relayStage -Force | Out-Null
    Invoke-DotNet @(
        "publish", (Join-Path $repositoryRoot "Survivalcraft.RelayServer\Survivalcraft.RelayServer.csproj"),
        "-c", "Release", "-f", "net10.0", "-r", "win-x64",
        "--self-contained", "false", "-o", $relayStage, "--nologo",
        "-p:ContinuousIntegrationBuild=true")

    $androidAssetDirectory = Join-Path $repositoryRoot "Survivalcraft.Android\obj\Release\net10.0-android\InternalMods"
    New-Item -ItemType Directory -Path $androidAssetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $multiplayerScmod -Destination (
        Join-Path $androidAssetDirectory "Survivalcraft.Multiplayer.scmod") -Force
    $androidBuildStarted = [System.DateTime]::UtcNow.AddSeconds(-2)
    Invoke-DotNet @(
        "publish", (Join-Path $repositoryRoot "Survivalcraft.Android\Survivalcraft.Android.csproj"),
        "-c", "Release", "-f", "net10.0-android", "-r", $releaseSpec.android.runtimeIdentifier,
        "--nologo", "-p:SkipNetworkModPackage=true",
        "-p:AndroidPackageFormat=apk", "-p:ContinuousIntegrationBuild=true")
    $androidApk = Get-ChildItem (Join-Path $repositoryRoot "Survivalcraft.Android\bin\Release") `
        -Filter "*-Signed.apk" -File -Recurse |
        Where-Object { $_.LastWriteTimeUtc -ge $androidBuildStarted } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $androidApk) {
        throw "A newly signed Android APK was not produced."
    }

    $windowsArtifact = Join-Path $outputRoot $releaseSpec.artifacts.windowsX64
    $relayArtifact = Join-Path $outputRoot $releaseSpec.artifacts.relayWindowsX64
    $androidArtifact = Join-Path $outputRoot $releaseSpec.artifacts.androidArm64
    New-DeterministicZip $windowsStage $windowsArtifact
    New-DeterministicZip $relayStage $relayArtifact
    Copy-Item -LiteralPath $androidApk.FullName -Destination $androidArtifact

    $releaseNotesSource = Join-Path $repositoryRoot "release\$($releaseSpec.tag).md"
    $releaseNotesDestination = Join-Path $outputRoot $releaseSpec.artifacts.releaseNotes
    Copy-Item -LiteralPath $releaseNotesSource -Destination $releaseNotesDestination

    $artifactRecords = @(
        [ordered]@{
            name = [string]$releaseSpec.artifacts.windowsX64
            kind = "windows-client"
            runtimeIdentifier = "win-x64"
            size = (Get-Item -LiteralPath $windowsArtifact).Length
            sha256 = Get-Sha256 $windowsArtifact
        },
        [ordered]@{
            name = [string]$releaseSpec.artifacts.relayWindowsX64
            kind = "relay-server"
            runtimeIdentifier = "win-x64"
            size = (Get-Item -LiteralPath $relayArtifact).Length
            sha256 = Get-Sha256 $relayArtifact
        },
        [ordered]@{
            name = [string]$releaseSpec.artifacts.androidArm64
            kind = "android-client"
            runtimeIdentifier = [string]$releaseSpec.android.runtimeIdentifier
            abi = [string]$releaseSpec.android.abi
            signing = [string]$releaseSpec.android.signing
            size = (Get-Item -LiteralPath $androidArtifact).Length
            sha256 = Get-Sha256 $androidArtifact
        }
    )
    $manifest = [ordered]@{
        schemaVersion = 1
        tag = [string]$releaseSpec.tag
        title = [string]$releaseSpec.title
        releaseVersion = [string]$releaseSpec.releaseVersion
        apiVersion = [string]$releaseSpec.apiVersion
        gameVersion = [string]$releaseSpec.gameVersion
        protocolRevision = [int]$releaseSpec.protocolRevision
        automationApiVersion = [int]$releaseSpec.automationApiVersion
        snapshotSchemaVersion = [int]$releaseSpec.snapshotSchemaVersion
        sourceCommit = $sourceCommit
        sourceCommitDate = $sourceDate
        sourceDirty = $sourceDirty
        reproducibility = [ordered]@{
            deterministicArchives = $true
            fixedArchiveTimestamp = "2000-01-01T00:00:00Z"
            androidBitForBitGuaranteed = $false
        }
        toolchain = [ordered]@{
            dotnetSdk = (& dotnet --version).Trim()
            os = [System.Environment]::OSVersion.VersionString
        }
        artifacts = $artifactRecords
    }
    $manifestPath = Join-Path $outputRoot $releaseSpec.artifacts.releaseManifest
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 8) + [System.Environment]::NewLine,
        $utf8NoBom)

    $checksumInputs = @(
        $windowsArtifact,
        $relayArtifact,
        $androidArtifact,
        $manifestPath,
        $releaseNotesDestination
    ) | Sort-Object { [System.IO.Path]::GetFileName($_) }
    $checksumLines = foreach ($file in $checksumInputs) {
        "$(Get-Sha256 $file) *$([System.IO.Path]::GetFileName($file))"
    }
    $checksumsPath = Join-Path $outputRoot $releaseSpec.artifacts.checksums
    [System.IO.File]::WriteAllText(
        $checksumsPath,
        ($checksumLines -join [System.Environment]::NewLine) + [System.Environment]::NewLine,
        $utf8NoBom)

    Write-Host "RELEASE BUILD PASSED"
    Write-Host "output=$outputRoot"
    Get-ChildItem -LiteralPath $outputRoot -File |
        Sort-Object Name |
        Select-Object Name, Length
}
finally {
    Pop-Location
}
