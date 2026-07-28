[CmdletBinding()]
param(
    [ValidateSet("fast", "doctor", "core", "compatibility", "relay", "full")]
    [string]$Mode = "fast",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$Artifacts,

    [switch]$UseAngle,

    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$fastProject = Join-Path $repositoryRoot "Survivalcraft.Testing.Tests\Survivalcraft.Testing.Tests.csproj"
$gameTestProject = Join-Path $repositoryRoot "Survivalcraft.Test\Survivalcraft.Test.csproj"
$hostProject = Join-Path $repositoryRoot "Survivalcraft.TestHost\Survivalcraft.TestHost.csproj"
$runtimeDirectory = Join-Path $repositoryRoot "Survivalcraft.Test\bin\$Configuration\net10.0-windows"
$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$coreArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\core-multiplayer-$runStamp"
}
elseif ($Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "core-multiplayer"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$compatibilityArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\compatibility-matrix-$runStamp"
}
elseif ($Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "compatibility-matrix"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$relayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\relay-mod-channel-$runStamp"
}
elseif ($Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "relay-mod-channel"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$buildProperties = if ($UseAngle) { @("-p:UseAngle=true") } else { @() }
$script:coreRuntimeBuilt = $false

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$CommandArguments)

    Write-Host "dotnet $($CommandArguments -join ' ')"
    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE"
    }
}

function Build-CoreRuntime {
    if ($NoBuild -or $script:coreRuntimeBuilt) {
        return
    }
    $gameBuildArguments = @(
        "build", $gameTestProject, "--nologo", "-c", $Configuration
    ) + $buildProperties
    Invoke-DotNet -CommandArguments $gameBuildArguments
    Invoke-DotNet -CommandArguments @(
        "build", $hostProject, "--nologo", "-c", $Configuration
    )
    $script:coreRuntimeBuilt = $true
}

function Run-FastTests {
    $neutralArguments = @(
        "test", $fastProject, "--nologo", "-c", $Configuration,
        "--filter", "Layer=L0|Layer=L1",
        "--logger", "console;verbosity=normal"
    )
    if ($NoBuild) {
        $neutralArguments += "--no-build"
    }
    Invoke-DotNet -CommandArguments $neutralArguments

    $runtimeArguments = @(
        "test", $gameTestProject, "--nologo", "-c", $Configuration,
        "--filter", "Layer=L0|Layer=L1",
        "--logger", "console;verbosity=normal"
    ) + $buildProperties
    if ($NoBuild) {
        $runtimeArguments += "--no-build"
    }
    Invoke-DotNet -CommandArguments $runtimeArguments
}

function Run-Doctor {
    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "doctor", "--runtime", $runtimeDirectory
    )
}

function Run-CoreScenario {
    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "core-multiplayer",
        "--runtime", $runtimeDirectory,
        "--artifacts", $coreArtifactDirectory
    )
}

function Run-CompatibilityScenario {
    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "compatibility-matrix",
        "--runtime", $runtimeDirectory,
        "--artifacts", $compatibilityArtifactDirectory
    )
}

function Run-RelayScenario {
    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "relay-mod-channel",
        "--runtime", $runtimeDirectory,
        "--artifacts", $relayArtifactDirectory
    )
}

Push-Location $repositoryRoot
try {
    switch ($Mode) {
        "fast" {
            Run-FastTests
        }
        "doctor" {
            Run-Doctor
        }
        "core" {
            Run-CoreScenario
        }
        "compatibility" {
            Run-CompatibilityScenario
        }
        "relay" {
            Run-RelayScenario
        }
        "full" {
            Run-FastTests
            Run-Doctor
            Run-CompatibilityScenario
            Run-CoreScenario
            Run-RelayScenario
            $legacyTestArguments = @(
                "test", $gameTestProject, "--nologo", "-c", $Configuration,
                "--filter", "Layer!=L2&Layer!=L0&Layer!=L1",
                "--logger", "console;verbosity=normal"
            ) + $buildProperties
            if ($NoBuild) {
                $legacyTestArguments += "--no-build"
            }
            Invoke-DotNet -CommandArguments $legacyTestArguments
        }
    }
}
finally {
    Pop-Location
}
