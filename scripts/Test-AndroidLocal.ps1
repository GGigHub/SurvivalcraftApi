[CmdletBinding()]
param(
    [string]$DeviceId,
    [string]$OutputDirectory,
    [ValidateRange(1, 20)]
    [int]$Cycles = 3,
    [ValidateRange(1, 20)]
    [int]$ReentryCount = 5,
    [switch]$NoBuild,
    [switch]$NoInstall,
    [switch]$OfflineCorpus,
    [switch]$SkipSourceTests,
    [switch]$SkipFaultInjection,
    [switch]$SkipModMatrix,
    [switch]$SkipDeviceConfiguration,
    [ValidateRange(10, 180)]
    [int]$WorldTimeoutSeconds = 90,
    [ValidateRange(10, 180)]
    [int]$RelayTimeoutSeconds = 90,
    [ValidateRange(0, 5)]
    [int]$RelayRestartCount = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$testResultsRoot = Join-Path $repositoryRoot "TestResults"
$runId = "{0:yyyyMMdd-HHmmss}" -f [DateTime]::Now
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $testResultsRoot "android-local\$runId"
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$summaryPath = Join-Path $artifactDirectory "summary.json"
$stabilityDirectory = Join-Path $artifactDirectory "device-stability"
$matrixDirectory = Join-Path $artifactDirectory "mod-matrix"
$startedUtc = [DateTimeOffset]::UtcNow
$steps = [System.Collections.Generic.List[object]]::new()
$stabilitySummary = $null
$matrixSummary = $null

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

function Write-Utf8Json {
    param([string]$Path, [object]$Value)
    [System.IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 50) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Add-SkippedStep {
    param([string]$Name, [string]$Reason)
    $steps.Add([ordered]@{
        name = $Name
        status = "skipped"
        startedUtc = $null
        completedUtc = $null
        durationSeconds = 0
        error = $Reason
    })
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    $stepStarted = [DateTimeOffset]::UtcNow
    Write-Host "Starting Android local step '$Name'."
    try {
        & $Action
        $steps.Add([ordered]@{
            name = $Name
            status = "passed"
            startedUtc = $stepStarted.ToString("O")
            completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
            durationSeconds = [Math]::Round(
                ([DateTimeOffset]::UtcNow - $stepStarted).TotalSeconds,
                3)
            error = ""
        })
    }
    catch {
        $steps.Add([ordered]@{
            name = $Name
            status = "failed"
            startedUtc = $stepStarted.ToString("O")
            completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
            durationSeconds = [Math]::Round(
                ([DateTimeOffset]::UtcNow - $stepStarted).TotalSeconds,
                3)
            error = $_.Exception.ToString()
        })
        throw
    }
}

function Invoke-DotNetTest {
    param([string]$Project, [string]$Filter)
    $arguments = @(
        "test",
        (Join-Path $repositoryRoot $Project),
        "-c", "Release",
        "--nologo",
        "--filter", $Filter)
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test '$Project' failed with exit code $LASTEXITCODE."
    }
}

function New-LocalSummary {
    param([bool]$Passed, [string]$ErrorMessage = "")
    return [ordered]@{
        schemaVersion = 1
        passed = $Passed
        error = $ErrorMessage
        startedUtc = $startedUtc.ToString("O")
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        requestedCycles = $Cycles
        reentryCountPerCycle = $ReentryCount
        sourceTestsSkipped = [bool]$SkipSourceTests
        modMatrixSkipped = [bool]$SkipModMatrix
        offlineCorpus = [bool]$OfflineCorpus
        steps = $steps
        deviceStability = if ($null -eq $stabilitySummary) {
            $null
        }
        else {
            [ordered]@{
                passed = $stabilitySummary.passed
                completedRelayCycles = $stabilitySummary.completedRelayCycles
                totalRelayAttempts = $stabilitySummary.totalRelayAttempts
                uniqueGlobalCodeCount = $stabilitySummary.uniqueGlobalCodeCount
                uniqueRoomCodeCount = $stabilitySummary.uniqueRoomCodeCount
                adbRecoveryCount = $stabilitySummary.adbRecoveryCount
                transportFailoverCount = $stabilitySummary.transportFailoverCount
                deviceRestoreFailureCount = $stabilitySummary.deviceRestoreFailureCount
                gameLogAuditedRunCount = $stabilitySummary.gameLogAuditedRunCount
                gameLogErrorCount = $stabilitySummary.gameLogErrorCount
                gameLogWarningCount = $stabilitySummary.gameLogWarningCount
                summaryPath = Join-Path $stabilityDirectory "summary.json"
            }
        }
        modMatrix = if ($null -eq $matrixSummary) {
            $null
        }
        else {
            [ordered]@{
                passed = $matrixSummary.passed
                selectedCaseCount = $matrixSummary.selectedCaseCount
                gatedCaseCount = $matrixSummary.gatedCaseCount
                failedGatedCaseCount = $matrixSummary.failedGatedCaseCount
                failedGatedCaseIds = $matrixSummary.failedGatedCaseIds
                gameLogAuditedCaseCount = $matrixSummary.gameLogAuditedCaseCount
                gameLogErrorCount = $matrixSummary.gameLogErrorCount
                gameLogWarningCount = $matrixSummary.gameLogWarningCount
                summaryPath = Join-Path $matrixDirectory "summary.json"
            }
        }
        sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        sourceDirty = @(& git -C $repositoryRoot status --porcelain).Count -gt 0
        artifactDirectory = $artifactDirectory
    }
}

if (-not (Test-PathInside $artifactDirectory $testResultsRoot)) {
    throw "OutputDirectory must be below '$testResultsRoot'."
}
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

try {
    if ($SkipSourceTests) {
        Add-SkippedStep "source-contracts" "SkipSourceTests was specified."
    }
    else {
        Invoke-Step "source-contracts" {
            Invoke-DotNetTest `
                "Survivalcraft.Testing.Tests\Survivalcraft.Testing.Tests.csproj" `
                "FullyQualifiedName~ReleaseContractTests"
            Invoke-DotNetTest `
                "Survivalcraft.Test\Survivalcraft.Test.csproj" `
                "FullyQualifiedName~RelayCandidateSelectionTest"
        }
    }

    if ($NoBuild) {
        Add-SkippedStep "build-test-apk" "NoBuild was specified."
    }
    else {
        Invoke-Step "build-test-apk" {
            & (Join-Path $PSScriptRoot "Build-AndroidDeviceTest.ps1")
        }
    }

    Invoke-Step "device-stability" {
        $parameters = @{
            OutputDirectory = $stabilityDirectory
            Cycles = $Cycles
            ReentryCount = $ReentryCount
            NoBuild = $true
            WorldTimeoutSeconds = $WorldTimeoutSeconds
            RelayTimeoutSeconds = $RelayTimeoutSeconds
            RelayRestartCount = $RelayRestartCount
        }
        if (-not [string]::IsNullOrWhiteSpace($DeviceId)) {
            $parameters.DeviceId = $DeviceId
        }
        if ($NoInstall) {
            $parameters.NoInstall = $true
        }
        if ($SkipDeviceConfiguration) {
            $parameters.SkipDeviceConfiguration = $true
        }
        if ($SkipFaultInjection) {
            $parameters.SkipFaultInjection = $true
        }
        & (Join-Path $PSScriptRoot "Test-AndroidDeviceStability.ps1") @parameters
        $stabilitySummaryPath = Join-Path $stabilityDirectory "summary.json"
        $script:stabilitySummary = Get-Content -LiteralPath $stabilitySummaryPath -Raw |
            ConvertFrom-Json -Depth 64
        if (-not $script:stabilitySummary.passed) {
            throw "Android device stability summary reported failure."
        }
    }

    if ($SkipModMatrix) {
        Add-SkippedStep "community-mod-matrix" "SkipModMatrix was specified."
    }
    else {
        Invoke-Step "community-mod-matrix" {
            $parameters = @{
                OutputDirectory = $matrixDirectory
                NoBuild = $true
                NoInstall = $true
                WorldTimeoutSeconds = $WorldTimeoutSeconds
                RelayTimeoutSeconds = $RelayTimeoutSeconds
                RelayRestartCount = $RelayRestartCount
            }
            if (-not [string]::IsNullOrWhiteSpace($DeviceId)) {
                $parameters.DeviceId = $DeviceId
            }
            if ($OfflineCorpus) {
                $parameters.Offline = $true
            }
            if ($SkipDeviceConfiguration) {
                $parameters.SkipDeviceConfiguration = $true
            }
            & (Join-Path $PSScriptRoot "Test-AndroidModMatrix.ps1") @parameters
            $matrixSummaryPath = Join-Path $matrixDirectory "summary.json"
            $script:matrixSummary = Get-Content -LiteralPath $matrixSummaryPath -Raw |
                ConvertFrom-Json -Depth 64
            if (-not $script:matrixSummary.passed) {
                throw "Android mod matrix summary reported failure."
            }
        }
    }

    Invoke-Step "final-device-cleanliness" {
        $resolvedDeviceId = if (-not [string]::IsNullOrWhiteSpace($DeviceId)) {
            $DeviceId
        }
        else {
            [string]$script:stabilitySummary.device.id
        }
        $adbPath = (Get-Command adb -ErrorAction Stop).Source
        $processOutput = @(& $adbPath -s $resolvedDeviceId shell pidof com.candy.survivalcraftAPI1_9.autotest 2>&1)
        if (-not [string]::IsNullOrWhiteSpace(($processOutput -join "").Trim())) {
            throw "Android test package process is still running: $($processOutput -join ' ')"
        }
        $worldOutput = @(& $adbPath -s $resolvedDeviceId shell (
            "if [ -d '/sdcard/Survivalcraft2.4_API1.9_AutoTest/Worlds' ]; " +
            "then find '/sdcard/Survivalcraft2.4_API1.9_AutoTest/Worlds' -mindepth 1 -maxdepth 1 -type d; fi") 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect final Android test worlds: $($worldOutput -join ' ')"
        }
        if (-not [string]::IsNullOrWhiteSpace(($worldOutput -join "").Trim())) {
            throw "Android test worlds remain after cleanup: $($worldOutput -join ', ')"
        }
        $modOutput = @(& $adbPath -s $resolvedDeviceId shell (
            "if [ -d '/sdcard/Survivalcraft2.4_API1.9_AutoTest/Mods' ]; " +
            "then find '/sdcard/Survivalcraft2.4_API1.9_AutoTest/Mods' -mindepth 1 -maxdepth 1 -type f; fi") 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect final Android test mods: $($modOutput -join ' ')"
        }
        if (-not [string]::IsNullOrWhiteSpace(($modOutput -join "").Trim())) {
            throw "Android test mod files remain after cleanup: $($modOutput -join ', ')"
        }
    }

    $summary = New-LocalSummary -Passed $true
    Write-Utf8Json $summaryPath $summary
    Write-Host "ANDROID LOCAL STABILITY PASSED"
    Write-Host "summary=$summaryPath"
}
catch {
    $failure = New-LocalSummary -Passed $false -ErrorMessage $_.Exception.ToString()
    Write-Utf8Json $summaryPath $failure
    Write-Host "Android local stability failed. Artifacts: $artifactDirectory"
    throw
}
