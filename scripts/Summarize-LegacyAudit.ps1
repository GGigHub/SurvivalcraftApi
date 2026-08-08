[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [string]$RepositoryRoot,

    [int]$DotnetExitCode = -1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    $RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$legacyBaselinePath = Join-Path $RepositoryRoot "Survivalcraft.Testing\Coverage\legacy-known-failures.json"

$trxFile = Get-ChildItem -LiteralPath $ArtifactDirectory -Filter "legacy-audit*.trx" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $trxFile) {
    throw "Legacy audit did not produce a TRX result in '$ArtifactDirectory'."
}

[xml]$trx = Get-Content -LiteralPath $trxFile.FullName -Raw
$namespace = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
$namespace.AddNamespace("t", $trx.DocumentElement.NamespaceURI)
$counters = $trx.SelectSingleNode("//t:ResultSummary/t:Counters", $namespace)
if ($null -eq $counters) {
    throw "Legacy audit TRX does not contain result counters."
}
$allResults = @($trx.SelectNodes("//t:UnitTestResult", $namespace))
$executedTests = @(
    $allResults |
        ForEach-Object { $_.GetAttribute("testName") } |
        Sort-Object -Unique
)
$failedTests = @(
    $allResults |
        Where-Object { $_.GetAttribute("outcome") -eq "Failed" } |
        ForEach-Object { $_.GetAttribute("testName") } |
        Sort-Object -Unique
)

if (!(Test-Path -LiteralPath $legacyBaselinePath -PathType Leaf)) {
    throw "Legacy failure baseline not found: $legacyBaselinePath"
}
$baseline = Get-Content -LiteralPath $legacyBaselinePath -Raw | ConvertFrom-Json
if ($baseline.schemaVersion -ne 1 -or $baseline.policy -ne "new-failures-block") {
    throw "Legacy failure baseline has an unsupported schema or policy: $legacyBaselinePath"
}
$rawKnownFailures = @(
    $baseline.groups |
        ForEach-Object { $_.tests }
)
$knownFailures = @($rawKnownFailures | Sort-Object -Unique)
if ($knownFailures.Count -eq 0 -or $knownFailures.Count -ne $rawKnownFailures.Count) {
    throw "Legacy failure baseline must contain a non-empty unique test inventory."
}

$unexpectedFailures = @(
    $failedTests |
        Where-Object { $knownFailures -notcontains $_ }
)
$knownFailuresObserved = @(
    $failedTests |
        Where-Object { $knownFailures -contains $_ }
)
$resolvedKnownFailures = @(
    $knownFailures |
        Where-Object { $failedTests -notcontains $_ }
)
$missingBaselineTests = @(
    $knownFailures |
        Where-Object { $executedTests -notcontains $_ }
)
$total = [int]$counters.GetAttribute("total")
$executed = [int]$counters.GetAttribute("executed")
$failed = [int]$counters.GetAttribute("failed")
$completed = $total -gt 0 -and $executed -eq $total
$effectiveExitCode = if ($DotnetExitCode -ge 0) {
    $DotnetExitCode
}
elseif ($completed) {
    if ($failed -gt 0) { 1 } else { 0 }
}
else {
    -1
}
$baselineMatched = $unexpectedFailures.Count -eq 0 -and $missingBaselineTests.Count -eq 0
$gatePassed = $completed -and $baselineMatched -and ($effectiveExitCode -eq 0 -or $effectiveExitCode -eq 1)
$summary = [ordered]@{
    schemaVersion = 2
    policy = $baseline.policy
    baseline = "Survivalcraft.Testing/Coverage/legacy-known-failures.json"
    dotnetExitCode = $effectiveExitCode
    exitCodeInferred = $DotnetExitCode -lt 0
    total = $total
    executed = $executed
    passed = [int]$counters.GetAttribute("passed")
    failed = $failed
    completed = $completed
    knownFailureAllowance = $knownFailures.Count
    knownFailuresObserved = $knownFailuresObserved
    unexpectedFailures = $unexpectedFailures
    resolvedKnownFailures = $resolvedKnownFailures
    missingBaselineTests = $missingBaselineTests
    baselineMatched = $baselineMatched
    gatePassed = $gatePassed
    failedTests = $failedTests
    trx = $trxFile.Name
}
$summaryPath = Join-Path $ArtifactDirectory "legacy-audit-summary.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host "Legacy audit: total=$($summary.total), passed=$($summary.passed), failed=$($summary.failed), known=$($knownFailuresObserved.Count), unexpected=$($unexpectedFailures.Count), resolved=$($resolvedKnownFailures.Count), summary=$summaryPath"
if (!$gatePassed) {
    throw "Legacy audit gate failed: completed=$completed, unexpectedFailures=$($unexpectedFailures.Count), missingBaselineTests=$($missingBaselineTests.Count), dotnetExitCode=$effectiveExitCode"
}
Write-Host "LEGACY AUDIT BASELINE MATCHED"
