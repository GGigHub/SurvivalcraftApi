[CmdletBinding()]
param(
    [ValidateSet("fast", "trust", "doctor", "core", "compatibility", "relay", "v2-direct", "v2-relay", "v2", "m1-direct", "m1-relay", "m1", "motion-direct", "motion-relay", "motion", "streaming-direct", "streaming-relay", "streaming", "entity-direct", "entity-relay", "entity", "pickable-projectile-direct", "pickable-projectile-relay", "pickable-projectile", "inventory-health-direct", "inventory-health-relay", "inventory-health", "environment-mechanics-direct", "environment-mechanics-relay", "environment-mechanics", "mount-direct", "mount-relay", "mount", "logic-direct", "logic-relay", "logic", "session-direct", "session-relay", "session", "soak", "legacy", "full")]
    [string]$Mode = "fast",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$Artifacts,

    [ValidateRange(1, 10000)]
    [int]$SoakIterations = 11,

    [ValidateSet("direct", "relay")]
    [string]$SoakTransport = "direct",

    [int]$SoakSeed = 20260730,

    [switch]$SoakIncludeLoss,

    [string]$SoakReplaySchedule,

    [switch]$UseAngle,

    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$fastProject = Join-Path $repositoryRoot "Survivalcraft.Testing.Tests\Survivalcraft.Testing.Tests.csproj"
$gameTestProject = Join-Path $repositoryRoot "Survivalcraft.Test\Survivalcraft.Test.csproj"
$hostProject = Join-Path $repositoryRoot "Survivalcraft.TestHost\Survivalcraft.TestHost.csproj"
$runtimeDirectory = Join-Path $repositoryRoot "Survivalcraft.Test\bin\$Configuration\net10.0-windows\win-x64"
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
$v2DirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\creative-fly-v2-direct-$runStamp"
}
elseif ($Mode -eq "v2" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "creative-fly-v2-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$v2RelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\creative-fly-v2-relay-$runStamp"
}
elseif ($Mode -eq "v2" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "creative-fly-v2-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$m1DirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\terrain-interaction-m1-lifecycle-direct-$runStamp"
}
elseif ($Mode -eq "m1" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "terrain-interaction-m1-lifecycle-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$m1RelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\terrain-interaction-m1-lifecycle-relay-$runStamp"
}
elseif ($Mode -eq "m1" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "terrain-interaction-m1-lifecycle-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$motionDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\player-motion-lifecycle-direct-$runStamp"
}
elseif ($Mode -eq "motion" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "player-motion-lifecycle-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$motionRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\player-motion-lifecycle-relay-$runStamp"
}
elseif ($Mode -eq "motion" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "player-motion-lifecycle-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$streamingDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\terrain-streaming-revisions-direct-$runStamp"
}
elseif ($Mode -eq "streaming" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "terrain-streaming-revisions-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$streamingRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\terrain-streaming-revisions-relay-$runStamp"
}
elseif ($Mode -eq "streaming" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "terrain-streaming-revisions-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$entityDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\entity-creature-lifecycle-direct-$runStamp"
}
elseif ($Mode -eq "entity" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "entity-creature-lifecycle-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$entityRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\entity-creature-lifecycle-relay-$runStamp"
}
elseif ($Mode -eq "entity" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "entity-creature-lifecycle-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$pickableProjectileDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\pickable-projectile-direct-$runStamp"
}
elseif ($Mode -eq "pickable-projectile" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "pickable-projectile-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$pickableProjectileRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\pickable-projectile-relay-$runStamp"
}
elseif ($Mode -eq "pickable-projectile" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "pickable-projectile-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$inventoryHealthDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\inventory-health-lifecycle-direct-$runStamp"
}
elseif ($Mode -eq "inventory-health" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "inventory-health-lifecycle-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$inventoryHealthRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\inventory-health-lifecycle-relay-$runStamp"
}
elseif ($Mode -eq "inventory-health" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "inventory-health-lifecycle-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$environmentMechanicsDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\environment-mechanics-direct-$runStamp"
}
elseif ($Mode -eq "environment-mechanics" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "environment-mechanics-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$environmentMechanicsRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\environment-mechanics-relay-$runStamp"
}
elseif ($Mode -eq "environment-mechanics" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "environment-mechanics-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$mountDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\mount-direct-$runStamp"
}
elseif ($Mode -eq "mount" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "mount-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$mountRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\mount-relay-$runStamp"
}
elseif ($Mode -eq "mount" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "mount-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$logicDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\logic-direct-$runStamp"
}
elseif ($Mode -eq "logic" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "logic-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$logicRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\logic-relay-$runStamp"
}
elseif ($Mode -eq "logic" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "logic-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$sessionDirectArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\session-lifecycle-direct-$runStamp"
}
elseif ($Mode -eq "session" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "session-lifecycle-direct"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$sessionRelayArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\session-lifecycle-relay-$runStamp"
}
elseif ($Mode -eq "session" -or $Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "session-lifecycle-relay"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$legacyArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\legacy-audit-$runStamp"
}
elseif ($Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "legacy-audit"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}
$serializedBuildProperties = @("-m:1", "-p:UseSharedCompilation=false")
$buildProperties = @($serializedBuildProperties)
if ($UseAngle) {
    $buildProperties += "-p:UseAngle=true"
}
$script:coreRuntimeBuilt = $false
$script:hostBuilt = $false
$coverageReportPath = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\coverage-trust-$runStamp.json"
}
else {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "coverage-trust.json"
}
$soakArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\multiplayer-soak-$runStamp"
}
else {
    [System.IO.Path]::GetFullPath($Artifacts)
}

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$CommandArguments)

    Write-Host "dotnet $($CommandArguments -join ' ')"
    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE"
    }
}

function Build-AutomationHost {
    if ($NoBuild -or $script:hostBuilt) {
        return
    }
    $hostBuildArguments = @(
        "build", $hostProject, "--nologo", "-c", $Configuration
    ) + $serializedBuildProperties
    Invoke-DotNet -CommandArguments $hostBuildArguments
    $script:hostBuilt = $true
}

function Build-CoreRuntime {
    if ($NoBuild -or $script:coreRuntimeBuilt) {
        return
    }
    $gameBuildArguments = @(
        "build", $gameTestProject, "--nologo", "-c", $Configuration
    ) + $buildProperties
    Invoke-DotNet -CommandArguments $gameBuildArguments
    Build-AutomationHost
    $script:coreRuntimeBuilt = $true
}

function Run-FastTests {
    $neutralArguments = @(
        "test", $fastProject, "--nologo", "-c", $Configuration,
        "--filter", "Layer=L0|Layer=L1",
        "--logger", "console;verbosity=normal"
    ) + $serializedBuildProperties
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
    if (!$NoBuild -and $Mode -eq "full") {
        $script:coreRuntimeBuilt = $true
        Build-AutomationHost
    }
}

function Run-Doctor {
    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "doctor", "--runtime", $runtimeDirectory
    )
}

function Run-CoverageTrust {
    Build-AutomationHost
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "coverage", "--output", $coverageReportPath
    )
}

function Run-Soak {
    Build-CoreRuntime
    $soakArguments = @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "soak",
        "--runtime", $runtimeDirectory,
        "--artifacts", $soakArtifactDirectory
    )
    if ([string]::IsNullOrWhiteSpace($SoakReplaySchedule)) {
        $soakArguments += @(
            "--iterations", $SoakIterations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            "--transport", $SoakTransport,
            "--seed", $SoakSeed.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        )
        if ($SoakIncludeLoss) {
            $soakArguments += "--include-loss"
        }
    }
    else {
        $soakArguments += @(
            "--replay-schedule", ([System.IO.Path]::GetFullPath($SoakReplaySchedule))
        )
    }
    Invoke-DotNet -CommandArguments $soakArguments
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

function Run-V2GameplayScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "creative-fly-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-M1TerrainInteractionScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "terrain-interaction-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-PlayerMotionScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "player-motion-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-TerrainStreamingScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "terrain-streaming-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-EntityCreatureScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "entity-creature-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-PickableProjectileScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "pickable-projectile-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-InventoryHealthScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "inventory-health-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-EnvironmentMechanicsScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "environment-mechanics-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-MountScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "mount-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-LogicScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "logic-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-SessionLifecycleScenario {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("direct", "relay")]
        [string]$Transport,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    Build-CoreRuntime
    Invoke-DotNet -CommandArguments @(
        "run", "--project", $hostProject, "--no-build", "-c", $Configuration,
        "--", "run", "session-lifecycle-multiplayer",
        "--transport", $Transport,
        "--runtime", $runtimeDirectory,
        "--artifacts", $ArtifactDirectory
    )
}

function Run-LegacyAudit {
    New-Item -ItemType Directory -Force -Path $legacyArtifactDirectory | Out-Null
    $legacyTestArguments = @(
        "test", $gameTestProject, "--nologo", "-c", $Configuration,
        "--filter", "Layer!=L2&Layer!=L0&Layer!=L1",
        "--logger", "console;verbosity=normal",
        "--logger", "trx;LogFileName=legacy-audit.trx",
        "--results-directory", $legacyArtifactDirectory,
        "--blame-hang",
        "--blame-hang-timeout", "5m",
        "--blame-hang-dump-type", "none"
    ) + $buildProperties
    if ($NoBuild -or $script:coreRuntimeBuilt) {
        $legacyTestArguments += "--no-build"
    }
    Write-Host "dotnet $($legacyTestArguments -join ' ')"
    & dotnet @legacyTestArguments
    $legacyExitCode = $LASTEXITCODE
    & (Join-Path $PSScriptRoot "Summarize-LegacyAudit.ps1") `
        -ArtifactDirectory $legacyArtifactDirectory `
        -RepositoryRoot $repositoryRoot `
        -DotnetExitCode $legacyExitCode
}

Push-Location $repositoryRoot
try {
    switch ($Mode) {
        "fast" {
            Run-FastTests
        }
        "trust" {
            Run-CoverageTrust
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
        "v2-direct" {
            Run-V2GameplayScenario -Transport "direct" -ArtifactDirectory $v2DirectArtifactDirectory
        }
        "v2-relay" {
            Run-V2GameplayScenario -Transport "relay" -ArtifactDirectory $v2RelayArtifactDirectory
        }
        "v2" {
            Run-V2GameplayScenario -Transport "direct" -ArtifactDirectory $v2DirectArtifactDirectory
            Run-V2GameplayScenario -Transport "relay" -ArtifactDirectory $v2RelayArtifactDirectory
        }
        "m1-direct" {
            Run-M1TerrainInteractionScenario -Transport "direct" -ArtifactDirectory $m1DirectArtifactDirectory
        }
        "m1-relay" {
            Run-M1TerrainInteractionScenario -Transport "relay" -ArtifactDirectory $m1RelayArtifactDirectory
        }
        "m1" {
            Run-M1TerrainInteractionScenario -Transport "direct" -ArtifactDirectory $m1DirectArtifactDirectory
            Run-M1TerrainInteractionScenario -Transport "relay" -ArtifactDirectory $m1RelayArtifactDirectory
        }
        "motion-direct" {
            Run-PlayerMotionScenario -Transport "direct" -ArtifactDirectory $motionDirectArtifactDirectory
        }
        "motion-relay" {
            Run-PlayerMotionScenario -Transport "relay" -ArtifactDirectory $motionRelayArtifactDirectory
        }
        "motion" {
            Run-PlayerMotionScenario -Transport "direct" -ArtifactDirectory $motionDirectArtifactDirectory
            Run-PlayerMotionScenario -Transport "relay" -ArtifactDirectory $motionRelayArtifactDirectory
        }
        "streaming-direct" {
            Run-TerrainStreamingScenario -Transport "direct" -ArtifactDirectory $streamingDirectArtifactDirectory
        }
        "streaming-relay" {
            Run-TerrainStreamingScenario -Transport "relay" -ArtifactDirectory $streamingRelayArtifactDirectory
        }
        "streaming" {
            Run-TerrainStreamingScenario -Transport "direct" -ArtifactDirectory $streamingDirectArtifactDirectory
            Run-TerrainStreamingScenario -Transport "relay" -ArtifactDirectory $streamingRelayArtifactDirectory
        }
        "entity-direct" {
            Run-EntityCreatureScenario -Transport "direct" -ArtifactDirectory $entityDirectArtifactDirectory
        }
        "entity-relay" {
            Run-EntityCreatureScenario -Transport "relay" -ArtifactDirectory $entityRelayArtifactDirectory
        }
        "entity" {
            Run-EntityCreatureScenario -Transport "direct" -ArtifactDirectory $entityDirectArtifactDirectory
            Run-EntityCreatureScenario -Transport "relay" -ArtifactDirectory $entityRelayArtifactDirectory
        }
        "pickable-projectile-direct" {
            Run-PickableProjectileScenario -Transport "direct" -ArtifactDirectory $pickableProjectileDirectArtifactDirectory
        }
        "pickable-projectile-relay" {
            Run-PickableProjectileScenario -Transport "relay" -ArtifactDirectory $pickableProjectileRelayArtifactDirectory
        }
        "pickable-projectile" {
            Run-PickableProjectileScenario -Transport "direct" -ArtifactDirectory $pickableProjectileDirectArtifactDirectory
            Run-PickableProjectileScenario -Transport "relay" -ArtifactDirectory $pickableProjectileRelayArtifactDirectory
        }
        "inventory-health-direct" {
            Run-InventoryHealthScenario -Transport "direct" -ArtifactDirectory $inventoryHealthDirectArtifactDirectory
        }
        "inventory-health-relay" {
            Run-InventoryHealthScenario -Transport "relay" -ArtifactDirectory $inventoryHealthRelayArtifactDirectory
        }
        "inventory-health" {
            Run-InventoryHealthScenario -Transport "direct" -ArtifactDirectory $inventoryHealthDirectArtifactDirectory
            Run-InventoryHealthScenario -Transport "relay" -ArtifactDirectory $inventoryHealthRelayArtifactDirectory
        }
        "environment-mechanics-direct" {
            Run-EnvironmentMechanicsScenario -Transport "direct" -ArtifactDirectory $environmentMechanicsDirectArtifactDirectory
        }
        "environment-mechanics-relay" {
            Run-EnvironmentMechanicsScenario -Transport "relay" -ArtifactDirectory $environmentMechanicsRelayArtifactDirectory
        }
        "environment-mechanics" {
            Run-EnvironmentMechanicsScenario -Transport "direct" -ArtifactDirectory $environmentMechanicsDirectArtifactDirectory
            Run-EnvironmentMechanicsScenario -Transport "relay" -ArtifactDirectory $environmentMechanicsRelayArtifactDirectory
        }
        "mount-direct" {
            Run-MountScenario -Transport "direct" -ArtifactDirectory $mountDirectArtifactDirectory
        }
        "mount-relay" {
            Run-MountScenario -Transport "relay" -ArtifactDirectory $mountRelayArtifactDirectory
        }
        "mount" {
            Run-MountScenario -Transport "direct" -ArtifactDirectory $mountDirectArtifactDirectory
            Run-MountScenario -Transport "relay" -ArtifactDirectory $mountRelayArtifactDirectory
        }
        "logic-direct" {
            Run-LogicScenario -Transport "direct" -ArtifactDirectory $logicDirectArtifactDirectory
        }
        "logic-relay" {
            Run-LogicScenario -Transport "relay" -ArtifactDirectory $logicRelayArtifactDirectory
        }
        "logic" {
            Run-LogicScenario -Transport "direct" -ArtifactDirectory $logicDirectArtifactDirectory
            Run-LogicScenario -Transport "relay" -ArtifactDirectory $logicRelayArtifactDirectory
        }
        "session-direct" {
            Run-SessionLifecycleScenario -Transport "direct" -ArtifactDirectory $sessionDirectArtifactDirectory
        }
        "session-relay" {
            Run-SessionLifecycleScenario -Transport "relay" -ArtifactDirectory $sessionRelayArtifactDirectory
        }
        "session" {
            Run-SessionLifecycleScenario -Transport "direct" -ArtifactDirectory $sessionDirectArtifactDirectory
            Run-SessionLifecycleScenario -Transport "relay" -ArtifactDirectory $sessionRelayArtifactDirectory
        }
        "soak" {
            Run-Soak
        }
        "legacy" {
            Run-LegacyAudit
        }
        "full" {
            Run-FastTests
            Run-CoverageTrust
            Run-Doctor
            Run-CompatibilityScenario
            Run-CoreScenario
            Run-RelayScenario
            Run-V2GameplayScenario -Transport "direct" -ArtifactDirectory $v2DirectArtifactDirectory
            Run-V2GameplayScenario -Transport "relay" -ArtifactDirectory $v2RelayArtifactDirectory
            Run-M1TerrainInteractionScenario -Transport "direct" -ArtifactDirectory $m1DirectArtifactDirectory
            Run-M1TerrainInteractionScenario -Transport "relay" -ArtifactDirectory $m1RelayArtifactDirectory
            Run-PlayerMotionScenario -Transport "direct" -ArtifactDirectory $motionDirectArtifactDirectory
            Run-PlayerMotionScenario -Transport "relay" -ArtifactDirectory $motionRelayArtifactDirectory
            Run-TerrainStreamingScenario -Transport "direct" -ArtifactDirectory $streamingDirectArtifactDirectory
            Run-TerrainStreamingScenario -Transport "relay" -ArtifactDirectory $streamingRelayArtifactDirectory
            Run-EntityCreatureScenario -Transport "direct" -ArtifactDirectory $entityDirectArtifactDirectory
            Run-EntityCreatureScenario -Transport "relay" -ArtifactDirectory $entityRelayArtifactDirectory
            Run-PickableProjectileScenario -Transport "direct" -ArtifactDirectory $pickableProjectileDirectArtifactDirectory
            Run-PickableProjectileScenario -Transport "relay" -ArtifactDirectory $pickableProjectileRelayArtifactDirectory
            Run-InventoryHealthScenario -Transport "direct" -ArtifactDirectory $inventoryHealthDirectArtifactDirectory
            Run-InventoryHealthScenario -Transport "relay" -ArtifactDirectory $inventoryHealthRelayArtifactDirectory
            Run-EnvironmentMechanicsScenario -Transport "direct" -ArtifactDirectory $environmentMechanicsDirectArtifactDirectory
            Run-EnvironmentMechanicsScenario -Transport "relay" -ArtifactDirectory $environmentMechanicsRelayArtifactDirectory
            Run-MountScenario -Transport "direct" -ArtifactDirectory $mountDirectArtifactDirectory
            Run-MountScenario -Transport "relay" -ArtifactDirectory $mountRelayArtifactDirectory
            Run-LogicScenario -Transport "direct" -ArtifactDirectory $logicDirectArtifactDirectory
            Run-LogicScenario -Transport "relay" -ArtifactDirectory $logicRelayArtifactDirectory
            Run-SessionLifecycleScenario -Transport "direct" -ArtifactDirectory $sessionDirectArtifactDirectory
            Run-SessionLifecycleScenario -Transport "relay" -ArtifactDirectory $sessionRelayArtifactDirectory
            Run-LegacyAudit
        }
    }
}
finally {
    Pop-Location
}
