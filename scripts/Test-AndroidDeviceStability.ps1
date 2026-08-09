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
    [switch]$SkipDeviceConfiguration,
    [switch]$SkipFaultInjection,
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
$runner = Join-Path $PSScriptRoot "Test-AndroidDevice.ps1"
$runId = "{0:yyyyMMdd-HHmmss}" -f [DateTime]::Now
$testResultsRoot = Join-Path $repositoryRoot "TestResults"
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $testResultsRoot "android-device-stability\$runId"
}
else {
    $candidate = [System.IO.Path]::GetFullPath($OutputDirectory)
    $normalizedResultsRoot = [System.IO.Path]::GetFullPath($testResultsRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if (-not $candidate.StartsWith(
        $normalizedResultsRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory must be below '$testResultsRoot'."
    }
    $candidate
}
$runsDirectory = Join-Path $artifactDirectory "runs"
$summaryPath = Join-Path $artifactDirectory "summary.json"
$startedUtc = [DateTimeOffset]::UtcNow
$runSummaries = [System.Collections.Generic.List[object]]::new()

function Write-Utf8Json {
    param([string]$Path, [object]$Value)
    [System.IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 30) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Invoke-DeviceRun {
    param(
        [string]$Name,
        [string]$Mode,
        [switch]$Install,
        [int]$FaultSequence = 0
    )
    $outputDirectory = Join-Path $runsDirectory $Name
    $parameters = @{
        Mode = $Mode
        NoBuild = $true
        OutputDirectory = $outputDirectory
        WorldTimeoutSeconds = $WorldTimeoutSeconds
        RelayTimeoutSeconds = $RelayTimeoutSeconds
        RelayRestartCount = $RelayRestartCount
    }
    if (-not [string]::IsNullOrWhiteSpace($DeviceId)) {
        $parameters.DeviceId = $DeviceId
    }
    if (-not $Install) {
        $parameters.NoInstall = $true
    }
    if ($SkipDeviceConfiguration) {
        $parameters.SkipDeviceConfiguration = $true
    }
    if ($Mode -eq "relay-reentry-pad") {
        $parameters.ReentryCount = $ReentryCount
    }
    if ($FaultSequence -gt 0) {
        $parameters.InjectAdbFailureAtSequence = $FaultSequence
    }

    Write-Host "Starting Android stability run '$Name' ($Mode)."
    $childFailure = $null
    try {
        & $runner @parameters
    }
    catch {
        $childFailure = $_
    }
    $childSummaryPath = Join-Path $outputDirectory "summary.json"
    if (-not (Test-Path -LiteralPath $childSummaryPath -PathType Leaf)) {
        throw "Android test run '$Name' did not produce '$childSummaryPath'."
    }
    $summary = Get-Content -LiteralPath $childSummaryPath -Raw | ConvertFrom-Json
    $runSummaries.Add([ordered]@{
        name = $Name
        mode = $Mode
        summaryPath = $childSummaryPath
        summary = $summary
    })
    if ($null -ne $childFailure) {
        throw $childFailure
    }
    if (-not $summary.passed) {
        throw "Android test run '$Name' reported failure."
    }
    return $summary
}

function New-AggregateSummary {
    param([bool]$Passed, [string]$ErrorMessage = "")
    $relayRuns = [System.Collections.Generic.List[object]]::new()
    foreach ($run in $runSummaries) {
        if ($run.mode -ne "relay-reentry-pad" -or $null -eq $run.summary.relayRuns) {
            continue
        }
        foreach ($relay in $run.summary.relayRuns) {
            $relayRuns.Add([ordered]@{
                run = $run.name
                iteration = $relay.iteration
                kind = $relay.kind
                code = $relay.code
                roomCode = $relay.roomCode
                moveControlMode = $relay.moveControlMode
                relayEndPoint = $relay.relayEndPoint
                relayAttemptCount = $relay.relayAttemptCount
                relayLastFailure = $relay.relayLastFailure
                restartCount = $relay.restartCount
            })
        }
    }
    $globalCodes = @($relayRuns | ForEach-Object { [string]$_.code } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $roomCodes = @($relayRuns | ForEach-Object { [string]$_.roomCode } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $recoveries = @($runSummaries | ForEach-Object { @($_.summary.adbRecoveryEvents) })
    $faults = @($runSummaries | ForEach-Object { @($_.summary.faultInjectionEvents) })
    $relayRecoveries = @($runSummaries | ForEach-Object { @($_.summary.relayRecoveryEvents) })
    $runtimeLogAudits = @($runSummaries | ForEach-Object {
        if ($null -ne $_.summary.PSObject.Properties['runtimeLogAudit']) {
            $_.summary.runtimeLogAudit
        }
    } | Where-Object { $null -ne $_ -and [bool]$_.available })
    $deviceRestoreFailures = @($runSummaries | Where-Object {
        $null -eq $_.summary.PSObject.Properties['deviceInteractive'] -or
        -not [bool]$_.summary.deviceInteractive.restored -or
        -not [string]::IsNullOrWhiteSpace([string]$_.summary.deviceInteractive.restoreError)
    } | ForEach-Object { $_.name })
    $transportFailoverCount = 0
    $runnerRelayRestartCount = 0
    foreach ($relayRun in $relayRuns) {
        if ([int]$relayRun["relayAttemptCount"] -gt 1) {
            $transportFailoverCount++
        }
        $runnerRelayRestartCount += [int]$relayRun["restartCount"]
    }
    $doctor = @($runSummaries | Where-Object { $_.mode -eq "doctor" } |
        Select-Object -First 1)
    return [ordered]@{
        schemaVersion = 1
        passed = $Passed
        error = $ErrorMessage
        startedUtc = $startedUtc.ToString("O")
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        requestedCycles = $Cycles
        completedRelayCycles = @($runSummaries | Where-Object {
            $_.mode -eq "relay-reentry-pad"
        }).Count
        reentryCountPerCycle = $ReentryCount
        childRunCount = $runSummaries.Count
        device = if ($doctor.Count -eq 1) { $doctor[0].summary.device } else { $null }
        deviceConfiguration = if ($doctor.Count -eq 1) {
            $doctor[0].summary.deviceConfiguration
        }
        else { $null }
        deviceInteractive = if ($doctor.Count -eq 1) {
            $doctor[0].summary.deviceInteractive
        }
        else { $null }
        deviceRestoreFailureCount = $deviceRestoreFailures.Count
        deviceRestoreFailureRuns = $deviceRestoreFailures
        totalRelayAttempts = $relayRuns.Count
        transportFailoverCount = $transportFailoverCount
        runnerRelayRestartCount = $runnerRelayRestartCount
        uniqueGlobalCodeCount = $globalCodes.Count
        uniqueRoomCodeCount = $roomCodes.Count
        globalCodes = $globalCodes
        roomCodes = $roomCodes
        adbRecoveryCount = $recoveries.Count
        faultInjectionCount = $faults.Count
        relayRecoveryCount = $relayRecoveries.Count
        gameLogAuditedRunCount = $runtimeLogAudits.Count
        gameLogErrorCount = @($runtimeLogAudits | ForEach-Object { [int]$_.errorCount } | Measure-Object -Sum).Sum
        gameLogWarningCount = @($runtimeLogAudits | ForEach-Object { [int]$_.warningCount } | Measure-Object -Sum).Sum
        relayRuns = $relayRuns
        runs = @($runSummaries | ForEach-Object {
            [ordered]@{
                name = $_.name
                mode = $_.mode
                passed = $_.summary.passed
                startedUtc = $_.summary.startedUtc
                completedUtc = $_.summary.completedUtc
                runtimeLogAudit = $_.summary.runtimeLogAudit
                summaryPath = $_.summaryPath
            }
        })
        sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        sourceDirty = @(& git -C $repositoryRoot status --porcelain).Count -gt 0
        artifactDirectory = $artifactDirectory
    }
}

New-Item -ItemType Directory -Path $runsDirectory -Force | Out-Null

try {
    if (!$NoBuild) {
        & (Join-Path $PSScriptRoot "Build-AndroidDeviceTest.ps1")
    }
    $null = Invoke-DeviceRun -Name "00-doctor" -Mode "doctor" -Install:(-not $NoInstall)
    if (!$SkipFaultInjection) {
        $null = Invoke-DeviceRun -Name "01-adb-recovery" -Mode "doctor" -FaultSequence 2
    }
    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        $null = Invoke-DeviceRun -Name ("{0:D2}-relay" -f ($cycle + 1)) -Mode "relay-reentry-pad"
    }
    $summary = New-AggregateSummary -Passed $true
    if ($summary.completedRelayCycles -ne $Cycles) {
        throw "Expected $Cycles completed relay cycles, got $($summary.completedRelayCycles)."
    }
    $expectedRelayAttempts = $Cycles * ($ReentryCount + 1)
    if ($summary.totalRelayAttempts -ne $expectedRelayAttempts) {
        throw "Expected $expectedRelayAttempts relay attempts, got $($summary.totalRelayAttempts)."
    }
    if (!$SkipFaultInjection -and (
        $summary.adbRecoveryCount -lt 1 -or $summary.faultInjectionCount -lt 1)) {
        throw "ADB fault injection did not produce a recorded recovery."
    }
    if ($summary.deviceRestoreFailureCount -ne 0) {
        throw "Device interactive settings were not restored after: $($summary.deviceRestoreFailureRuns -join ', ')."
    }
    if ($summary.gameLogAuditedRunCount -ne $summary.childRunCount -or
        $summary.gameLogErrorCount -ne 0) {
        throw "Game.log audit coverage or error count is invalid across stability runs."
    }
    Write-Utf8Json $summaryPath $summary
    Write-Host "ANDROID DEVICE STABILITY PASSED"
    Write-Host "relayAttempts=$($summary.totalRelayAttempts)"
    Write-Host "adbRecoveries=$($summary.adbRecoveryCount)"
    Write-Host "artifacts=$artifactDirectory"
}
catch {
    $primaryFailure = $_
    try {
        $failure = New-AggregateSummary -Passed $false -ErrorMessage $primaryFailure.Exception.ToString()
        Write-Utf8Json $summaryPath $failure
    }
    catch {
        Write-Utf8Json $summaryPath ([ordered]@{
            schemaVersion = 1
            passed = $false
            error = $primaryFailure.Exception.ToString()
            aggregateError = $_.Exception.ToString()
            startedUtc = $startedUtc.ToString("O")
            completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
            artifactDirectory = $artifactDirectory
        })
    }
    Write-Host "Android device stability failed. Artifacts: $artifactDirectory"
    throw $primaryFailure
}
