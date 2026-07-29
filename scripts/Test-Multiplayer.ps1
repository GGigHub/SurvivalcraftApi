[CmdletBinding()]
param(
    [ValidateSet("fast", "doctor", "core", "compatibility", "relay", "v2-direct", "v2-relay", "v2", "m1-direct", "m1-relay", "m1", "motion-direct", "motion-relay", "motion", "streaming-direct", "streaming-relay", "streaming", "entity-direct", "entity-relay", "entity", "inventory-health-direct", "inventory-health-relay", "inventory-health", "environment-mechanics-direct", "environment-mechanics-relay", "environment-mechanics", "legacy", "full")]
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
$legacyArtifactDirectory = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repositoryRoot "TestResults\legacy-audit-$runStamp"
}
elseif ($Mode -eq "full") {
    Join-Path ([System.IO.Path]::GetFullPath($Artifacts)) "legacy-audit"
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

function Run-LegacyAudit {
    New-Item -ItemType Directory -Force -Path $legacyArtifactDirectory | Out-Null
    $legacyTestArguments = @(
        "test", $gameTestProject, "--nologo", "-c", $Configuration,
        "--filter", "Layer!=L2&Layer!=L0&Layer!=L1",
        "--logger", "console;verbosity=normal",
        "--logger", "trx;LogFileName=legacy-audit.trx",
        "--results-directory", $legacyArtifactDirectory
    ) + $buildProperties
    if ($NoBuild) {
        $legacyTestArguments += "--no-build"
    }
    Write-Host "dotnet $($legacyTestArguments -join ' ')"
    & dotnet @legacyTestArguments
    $legacyExitCode = $LASTEXITCODE
    $trxFile = Get-ChildItem -LiteralPath $legacyArtifactDirectory -Filter "legacy-audit*.trx" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $trxFile) {
        throw "Legacy audit did not produce a TRX result (dotnet exit code $legacyExitCode)."
    }

    [xml]$trx = Get-Content -LiteralPath $trxFile.FullName -Raw
    $namespace = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $namespace.AddNamespace("t", $trx.DocumentElement.NamespaceURI)
    $counters = $trx.SelectSingleNode("//t:ResultSummary/t:Counters", $namespace)
    $failedTests = @(
        $trx.SelectNodes("//t:UnitTestResult[@outcome='Failed']", $namespace) |
            ForEach-Object { $_.GetAttribute("testName") } |
            Sort-Object -Unique
    )
    $summary = [ordered]@{
        schemaVersion = 1
        dotnetExitCode = $legacyExitCode
        total = [int]$counters.GetAttribute("total")
        executed = [int]$counters.GetAttribute("executed")
        passed = [int]$counters.GetAttribute("passed")
        failed = [int]$counters.GetAttribute("failed")
        failedTests = $failedTests
        trx = $trxFile.Name
    }
    $summaryPath = Join-Path $legacyArtifactDirectory "legacy-audit-summary.json"
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Write-Host "Legacy audit: total=$($summary.total), passed=$($summary.passed), failed=$($summary.failed), summary=$summaryPath"
    if ($legacyExitCode -ne 0) {
        throw "dotnet exited with code $legacyExitCode"
    }
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
        "legacy" {
            Run-LegacyAudit
        }
        "full" {
            Run-FastTests
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
            Run-InventoryHealthScenario -Transport "direct" -ArtifactDirectory $inventoryHealthDirectArtifactDirectory
            Run-InventoryHealthScenario -Transport "relay" -ArtifactDirectory $inventoryHealthRelayArtifactDirectory
            Run-EnvironmentMechanicsScenario -Transport "direct" -ArtifactDirectory $environmentMechanicsDirectArtifactDirectory
            Run-EnvironmentMechanicsScenario -Transport "relay" -ArtifactDirectory $environmentMechanicsRelayArtifactDirectory
            Run-LegacyAudit
        }
    }
}
finally {
    Pop-Location
}
