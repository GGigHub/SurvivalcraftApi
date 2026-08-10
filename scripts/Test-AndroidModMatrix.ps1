[CmdletBinding()]
param(
    [string]$DeviceId,
    [string]$ManifestPath,
    [string]$LockPath,
    [string]$CorpusDirectory,
    [string]$OutputDirectory,
    [string[]]$CaseId = @(),
    [switch]$Offline,
    [switch]$NoBuild,
    [switch]$NoInstall,
    [switch]$WorldOnly,
    [switch]$SkipDeviceConfiguration,
    [ValidateRange(10, 180)]
    [int]$BridgeTimeoutSeconds = 90,
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
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$ManifestPath = if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    Join-Path $repositoryRoot "tests\android-mod-corpus\manifest.json"
}
else {
    [System.IO.Path]::GetFullPath($ManifestPath)
}
$ManifestPath = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).Path
$LockPath = if ([string]::IsNullOrWhiteSpace($LockPath)) {
    Join-Path (Split-Path -Parent $ManifestPath) "lock.json"
}
else {
    [System.IO.Path]::GetFullPath($LockPath)
}
$corpusRoot = if ([string]::IsNullOrWhiteSpace($CorpusDirectory)) {
    Join-Path $artifactsRoot "android-mod-corpus"
}
else {
    [System.IO.Path]::GetFullPath($CorpusDirectory)
}
$runId = "{0:yyyyMMdd-HHmmss}" -f [DateTime]::Now
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $testResultsRoot "android-mod-matrix\$runId"
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$runsDirectory = Join-Path $artifactDirectory "runs"
$summaryPath = Join-Path $artifactDirectory "summary.json"
$runner = Join-Path $PSScriptRoot "Test-AndroidDevice.ps1"
$resolver = Join-Path $PSScriptRoot "Resolve-AndroidModCorpus.ps1"
$binaryCompatibilityRunner = Join-Path $PSScriptRoot "Test-ModBinaryCompatibility.ps1"
$startedUtc = [DateTimeOffset]::UtcNow
$caseResults = [System.Collections.Generic.List[object]]::new()
$baselineResult = $null
$cleanupResult = $null
$script:MaximumModPackageBytes = 67108864

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

function Get-PropertyValue {
    param([object]$Object, [string]$Name, [object]$DefaultValue = $null)
    if ($null -eq $Object) {
        return $DefaultValue
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $DefaultValue
    }
    return $property.Value
}

function Invoke-DeviceRunner {
    param(
        [string]$Name,
        [string]$Mode,
        [string[]]$ModPaths = @(),
        [string[]]$ExpectedPackages = @(),
        [object[]]$ClassSubstituteSelections = @(),
        [object[]]$StartupDialogActions = @(),
        [string[]]$WorldProbes = @(),
        [int]$ReentryCount = 1,
        [switch]$Install
    )
    $outputPath = Join-Path $runsDirectory $Name
    $parameters = @{
        Mode = $Mode
        NoBuild = $true
        OutputDirectory = $outputPath
        BridgeTimeoutSeconds = $BridgeTimeoutSeconds
        WorldTimeoutSeconds = $WorldTimeoutSeconds
        RelayTimeoutSeconds = $RelayTimeoutSeconds
        RelayRestartCount = $RelayRestartCount
        MaximumModPackageBytes = $script:MaximumModPackageBytes
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
    if ($Mode -in @("world-reentry-pad", "relay-reentry-pad")) {
        $parameters.ReentryCount = $ReentryCount
    }
    if ($ModPaths.Count -gt 0) {
        $parameters.ModPackagePath = $ModPaths
        $parameters.ExpectedModPackageName = $ExpectedPackages
    }
    if ($ClassSubstituteSelections.Count -gt 0) {
        $parameters.ClassSubstituteSelection = $ClassSubstituteSelections
    }
    if ($StartupDialogActions.Count -gt 0) {
        $parameters.StartupDialogAction = $StartupDialogActions
    }
    if ($WorldProbes.Count -gt 0) {
        $parameters.WorldProbe = $WorldProbes
    }
    $runnerException = $null
    try {
        & $runner @parameters
    }
    catch {
        $runnerException = $_.Exception
    }
    $childSummaryPath = Join-Path $outputPath "summary.json"
    if (-not (Test-Path -LiteralPath $childSummaryPath -PathType Leaf)) {
        return [ordered]@{
            name = $Name
            passed = $false
            summaryPath = $childSummaryPath
            summary = $null
            error = if ($null -eq $runnerException) {
                "Device runner did not produce a summary."
            }
            else {
                $runnerException.ToString()
            }
        }
    }
    $childSummary = Get-Content -LiteralPath $childSummaryPath -Raw | ConvertFrom-Json -Depth 64
    return [ordered]@{
        name = $Name
        passed = [bool]$childSummary.passed -and $null -eq $runnerException
        summaryPath = $childSummaryPath
        summary = $childSummary
        error = if ($null -eq $runnerException) { "" } else { $runnerException.ToString() }
    }
}

function New-MatrixSummary {
    param([bool]$Passed, [string]$ErrorMessage = "")
    $gatedCases = @($caseResults | Where-Object { [bool]$_['gate'] })
    $failedGatedCases = @($gatedCases | Where-Object { -not [bool]$_['passed'] })
    $observationalCases = @($caseResults | Where-Object { -not [bool]$_['gate'] })
    $failedObservationalCases = @($observationalCases | Where-Object { -not [bool]$_['passed'] })
    $failedCases = @($caseResults | Where-Object { -not [bool]$_['passed'] })
    $runtimeLogAudits = @($caseResults | ForEach-Object { $_['runtimeLogAudit'] } | Where-Object {
        $null -ne $_ -and [bool]$_.available
    })
    $modOutcomes = [System.Collections.Generic.List[object]]::new()
    if ($null -ne $script:ResolvedCorpus) {
        foreach ($mod in @($script:ResolvedCorpus.mods)) {
            $relatedCases = @($caseResults | Where-Object {
                @($_['modIds']) -contains [string]$mod.id
            })
            $subjectCases = @($caseResults | Where-Object {
                [string]::Equals(
                    [string]$_['subjectModId'],
                    [string]$mod.id,
                    [System.StringComparison]::OrdinalIgnoreCase)
            })
            $supportCases = @($relatedCases | Where-Object {
                -not [string]::Equals(
                    [string]$_['subjectModId'],
                    [string]$mod.id,
                    [System.StringComparison]::OrdinalIgnoreCase)
            })
            $modOutcomes.Add([ordered]@{
                id = $mod.id
                packageName = $mod.packageName
                version = $mod.version
                sha256 = $mod.sha256
                subjectCaseId = if ($subjectCases.Count -eq 1) { [string]$subjectCases[0]['id'] } else { "" }
                subjectCasePassed = if ($subjectCases.Count -eq 1) { [bool]$subjectCases[0]['passed'] } else { $null }
                supportCaseCount = $supportCases.Count
                caseCount = $relatedCases.Count
                passedCaseCount = @($relatedCases | Where-Object { [bool]$_['passed'] }).Count
                failedCaseIds = @($relatedCases | Where-Object { -not [bool]$_['passed'] } | ForEach-Object { $_['id'] })
            })
        }
    }
    return [ordered]@{
        schemaVersion = 1
        passed = $Passed
        error = $ErrorMessage
        startedUtc = $startedUtc.ToString("O")
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        manifestPath = $ManifestPath
        lockPath = $LockPath
        corpusDirectory = $corpusRoot
        offline = [bool]$Offline
        worldOnly = [bool]$WorldOnly
        deviceId = $DeviceId
        binaryCompatibility = $script:BinaryCompatibility
        baseline = $baselineResult
        cleanup = $cleanupResult
        selectedCaseCount = $caseResults.Count
        allCasesPassed = $failedCases.Count -eq 0
        failedCaseCount = $failedCases.Count
        failedCaseIds = @($failedCases | ForEach-Object { $_['id'] })
        gatedCaseCount = $gatedCases.Count
        failedGatedCaseCount = $failedGatedCases.Count
        failedGatedCaseIds = @($failedGatedCases | ForEach-Object { $_['id'] })
        observationalCaseCount = $observationalCases.Count
        failedObservationalCaseCount = $failedObservationalCases.Count
        failedObservationalCaseIds = @($failedObservationalCases | ForEach-Object { $_['id'] })
        gameLogAuditedCaseCount = $runtimeLogAudits.Count
        gameLogErrorCount = @($runtimeLogAudits | ForEach-Object { [int]$_.errorCount } | Measure-Object -Sum).Sum
        gameLogWarningCount = @($runtimeLogAudits | ForEach-Object { [int]$_.warningCount } | Measure-Object -Sum).Sum
        cases = $caseResults
        mods = $modOutcomes
        sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        sourceDirty = @(& git -C $repositoryRoot status --porcelain).Count -gt 0
        artifactDirectory = $artifactDirectory
    }
}

if (-not (Test-PathInside $artifactDirectory $testResultsRoot)) {
    throw "OutputDirectory must be below '$testResultsRoot'."
}
if (-not (Test-PathInside $corpusRoot $artifactsRoot)) {
    throw "CorpusDirectory must be below '$artifactsRoot'."
}
New-Item -ItemType Directory -Path $runsDirectory -Force | Out-Null

$script:ResolvedCorpus = $null
$script:BinaryCompatibility = $null
try {
    if (!$NoBuild) {
        & (Join-Path $PSScriptRoot "Build-AndroidDeviceTest.ps1")
    }

    $resolverParameters = @{
        ManifestPath = $ManifestPath
        LockPath = $LockPath
        OutputDirectory = $corpusRoot
    }
    if ($Offline) {
        $resolverParameters.Offline = $true
    }
    & $resolver @resolverParameters
    $resolutionPath = Join-Path $corpusRoot "resolution.json"
    if (-not (Test-Path -LiteralPath $resolutionPath -PathType Leaf)) {
        throw "Corpus resolver did not produce '$resolutionPath'."
    }
    $script:ResolvedCorpus = Get-Content -LiteralPath $resolutionPath -Raw | ConvertFrom-Json -Depth 64
    if (-not $script:ResolvedCorpus.passed) {
        throw "Corpus resolver reported failure."
    }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 64
    $manifestLimits = Get-PropertyValue $manifest "limits" $null
    [long]$script:MaximumModPackageBytes = Get-PropertyValue `
        $manifestLimits `
        "maximumDownloadBytes" `
        67108864
    if ($script:MaximumModPackageBytes -le 0 -or $script:MaximumModPackageBytes -gt 512MB) {
        throw "Manifest maximumDownloadBytes is outside the device runner safety bounds."
    }

    $resolvedById = @{}
    $resolvedIdByPackage = @{}
    foreach ($resolvedMod in @($script:ResolvedCorpus.mods)) {
        if ($resolvedById.ContainsKey([string]$resolvedMod.id)) {
            throw "Resolved corpus contains duplicate id '$($resolvedMod.id)'."
        }
        $resolvedById[[string]$resolvedMod.id] = $resolvedMod
        $resolvedIdByPackage[[string]$resolvedMod.packageName] = [string]$resolvedMod.id
    }

    $allCases = [System.Collections.Generic.List[object]]::new()
    $allCaseIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($case in @((Get-PropertyValue $manifest "testCases" @()))) {
        $id = ([string](Get-PropertyValue $case "id" "")).Trim()
        if ($id -notmatch "^[a-z0-9][a-z0-9._-]{0,63}$" -or -not $allCaseIds.Add($id)) {
            throw "Manifest test case id '$id' is invalid or duplicated."
        }
        $mode = ([string](Get-PropertyValue $case "mode" "doctor")).Trim()
        if ($mode -notin @("doctor", "world-reentry-pad", "relay-reentry-pad")) {
            throw "Manifest test case '$id' has unsupported mode '$mode'."
        }
        [int]$reentryCount = Get-PropertyValue $case "reentryCount" 1
        if ($reentryCount -lt 1 -or $reentryCount -gt 20) {
            throw "Manifest test case '$id' has invalid reentryCount $reentryCount."
        }
        $caseModIds = @((Get-PropertyValue $case "mods" @()) | ForEach-Object { ([string]$_).Trim() })
        if ($caseModIds.Count -eq 0 -or @($caseModIds | Sort-Object -Unique).Count -ne $caseModIds.Count) {
            throw "Manifest test case '$id' must contain one or more unique mod ids."
        }
        foreach ($modId in $caseModIds) {
            if (-not $resolvedById.ContainsKey($modId)) {
                throw "Manifest test case '$id' references unresolved mod '$modId'."
            }
        }
        foreach ($modId in $caseModIds) {
            $resolvedMod = $resolvedById[$modId]
            foreach ($dependencyPackage in @($resolvedMod.dependencies)) {
                if ($resolvedIdByPackage.ContainsKey([string]$dependencyPackage)) {
                    $dependencyId = $resolvedIdByPackage[[string]$dependencyPackage]
                    if ($caseModIds -notcontains $dependencyId) {
                        throw "Manifest test case '$id' omits corpus dependency '$dependencyId' required by '$modId'."
                    }
                }
            }
        }
        $subjectModId = ([string](Get-PropertyValue $case "subjectMod" "")).Trim()
        if ([string]::IsNullOrWhiteSpace($subjectModId) -and $caseModIds.Count -eq 1) {
            $subjectModId = $caseModIds[0]
        }
        if (-not [string]::IsNullOrWhiteSpace($subjectModId) -and
            $caseModIds -notcontains $subjectModId) {
            throw "Manifest test case '$id' subjectMod '$subjectModId' must also appear in mods."
        }
        $classSubstitutions = [System.Collections.Generic.List[object]]::new()
        $classSubstitutionGuids = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($selection in @((Get-PropertyValue $case "classSubstitutions" @()))) {
            $guid = ([string](Get-PropertyValue $selection "guid" "")).Trim()
            $packageName = ([string](Get-PropertyValue $selection "packageName" "")).Trim()
            $className = ([string](Get-PropertyValue $selection "className" "")).Trim()
            if ($guid -notmatch "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$" -or
                -not $classSubstitutionGuids.Add($guid)) {
                throw "Manifest test case '$id' has an invalid or duplicate class-substitute guid '$guid'."
            }
            if (-not $resolvedIdByPackage.ContainsKey($packageName) -or
                $caseModIds -notcontains [string]$resolvedIdByPackage[$packageName]) {
                throw "Manifest test case '$id' class substitute package '$packageName' is not staged by the case."
            }
            if ($className -notmatch "^[\p{L}_][\p{L}\p{N}_.+]{0,299}$") {
                throw "Manifest test case '$id' has invalid class substitute '$className'."
            }
            $classSubstitutions.Add([ordered]@{
                guid = $guid
                packageName = $packageName
                className = $className
            })
        }
        $startupDialogActions = [System.Collections.Generic.List[object]]::new()
        $startupDialogTypes = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal)
        foreach ($action in @((Get-PropertyValue $case "startupDialogActions" @()))) {
            $dialogType = ([string](Get-PropertyValue $action "dialogType" "")).Trim()
            $query = ([string](Get-PropertyValue $action "query" "")).Trim()
            if ($dialogType -notmatch "^[A-Za-z_][A-Za-z0-9_.+]{0,199}$" -or
                -not $startupDialogTypes.Add($dialogType)) {
                throw "Manifest test case '$id' has invalid or duplicate startup dialog '$dialogType'."
            }
            if ([string]::IsNullOrWhiteSpace($query) -or
                $query.Length -gt 200 -or $query -match "[\r\n]") {
                throw "Manifest test case '$id' has invalid startup-dialog query '$query'."
            }
            $startupDialogActions.Add([ordered]@{
                dialogType = $dialogType
                query = $query
            })
        }
        $worldProbes = @((Get-PropertyValue $case "worldProbes" @()) | ForEach-Object {
            ([string]$_).Trim()
        })
        if (@($worldProbes | Sort-Object -Unique).Count -ne $worldProbes.Count) {
            throw "Manifest test case '$id' contains duplicate world probes."
        }
        foreach ($worldProbe in $worldProbes) {
            if ($worldProbe -notin @("firearms-2.8")) {
                throw "Manifest test case '$id' has unsupported world probe '$worldProbe'."
            }
            if ($caseModIds -notcontains $worldProbe) {
                throw "Manifest test case '$id' world probe '$worldProbe' requires that mod id in the case."
            }
            if ($mode -notin @("world-reentry-pad", "relay-reentry-pad")) {
                throw "Manifest test case '$id' world probes require a world-reentry mode."
            }
        }
        $allCases.Add([ordered]@{
            id = $id
            mode = $mode
            reentryCount = $reentryCount
            gate = [bool](Get-PropertyValue $case "gate" $true)
            subjectModId = $subjectModId
            modIds = $caseModIds
            classSubstitutions = @($classSubstitutions)
            startupDialogActions = @($startupDialogActions)
            worldProbes = $worldProbes
        })
    }
    if ($allCases.Count -eq 0) {
        throw "Manifest has no testCases."
    }
    foreach ($resolvedId in $resolvedById.Keys) {
        $subjectCases = @($allCases | Where-Object {
            [string]::Equals(
                [string]$_['subjectModId'],
                [string]$resolvedId,
                [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($subjectCases.Count -ne 1) {
            throw "Resolved mod '$resolvedId' must be the subject of exactly one test case; found $($subjectCases.Count)."
        }
    }

    $requestedCaseIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($requestedCaseValue in @($CaseId)) {
        $requestedCase = ([string]$requestedCaseValue).Trim()
        if (-not $requestedCaseIds.Add($requestedCase)) {
            throw "CaseId '$requestedCase' was requested more than once."
        }
        if (-not $allCaseIds.Contains($requestedCase)) {
            throw "CaseId '$requestedCase' is not in the manifest."
        }
    }
    $selectedCases = @($allCases | Where-Object {
        $requestedCaseIds.Count -eq 0 -or $requestedCaseIds.Contains([string]$_['id'])
    })
    if ($selectedCases.Count -eq 0) {
        throw "No matrix cases were selected."
    }

    $selectedModernModIds = @($selectedCases |
        ForEach-Object { @($_['modIds']) } |
        Sort-Object -Unique |
        Where-Object {
            [string]$apiVersion = [string]$resolvedById[[string]$_].apiVersion
            $apiVersion -match "^1\.9(?:\.|$)"
        })
    if ($selectedModernModIds.Count -gt 0) {
        $binaryCompatibilityPath = Join-Path $artifactDirectory "binary-compatibility.json"
        $modernModPaths = @($selectedModernModIds | ForEach-Object {
            [string]$resolvedById[[string]$_].path
        })
        & $binaryCompatibilityRunner `
            -ModPackagePath $modernModPaths `
            -OutputPath $binaryCompatibilityPath
        $script:BinaryCompatibility = Get-Content `
            -LiteralPath $binaryCompatibilityPath `
            -Raw | ConvertFrom-Json -Depth 64
        if (-not [bool]$script:BinaryCompatibility.passed) {
            throw "Selected API 1.9 mod binaries are incompatible with the current host."
        }
    }

    Write-Host "Starting empty-corpus Android baseline."
    $baselineResult = Invoke-DeviceRunner -Name "00-baseline" -Mode "doctor" -Install:(-not $NoInstall)
    if (-not [bool]$baselineResult['passed']) {
        throw "Empty-corpus Android baseline failed."
    }

    $ordinal = 0
    foreach ($case in $selectedCases) {
        $ordinal++
        $caseIdValue = [string]$case['id']
        $mods = @($case['modIds'] | ForEach-Object { $resolvedById[[string]$_] })
        $paths = @($mods | ForEach-Object { [string]$_.path })
        $packages = @($mods | ForEach-Object { [string]$_.packageName })
        $runName = "{0:D2}-{1}" -f $ordinal, $caseIdValue
        $requestedMode = [string]$case['mode']
        $effectiveMode = if ($WorldOnly -and $requestedMode -eq "relay-reentry-pad") {
            "world-reentry-pad"
        }
        else {
            $requestedMode
        }
        Write-Host "Starting Android mod matrix case '$caseIdValue' with $($mods.Count) mod(s) in '$effectiveMode'."
        $run = Invoke-DeviceRunner `
            -Name $runName `
            -Mode $effectiveMode `
            -ModPaths $paths `
            -ExpectedPackages $packages `
            -ClassSubstituteSelections @($case['classSubstitutions']) `
            -StartupDialogActions @($case['startupDialogActions']) `
            -WorldProbes @($case['worldProbes']) `
            -ReentryCount ([int]$case['reentryCount'])
        $childSummary = $run['summary']
        $failureArtifacts = if ($null -ne $childSummary -and
            $null -ne $childSummary.PSObject.Properties['failureArtifacts']) {
            $childSummary.failureArtifacts
        }
        else {
            $null
        }
        $runtimeLogAudit = if ($null -ne $childSummary -and
            $null -ne $childSummary.PSObject.Properties['runtimeLogAudit']) {
            $childSummary.runtimeLogAudit
        }
        elseif ($null -ne $failureArtifacts -and
            $null -ne $failureArtifacts.PSObject.Properties['gameLogAudit']) {
            $failureArtifacts.gameLogAudit
        }
        else {
            $null
        }
        $caseResults.Add([ordered]@{
            id = $caseIdValue
            gate = [bool]$case['gate']
            requestedMode = $requestedMode
            mode = $effectiveMode
            reentryCount = [int]$case['reentryCount']
            subjectModId = [string]$case['subjectModId']
            modIds = @($case['modIds'])
            configuredClassSubstitutions = @($case['classSubstitutions'])
            appliedClassSubstitutions = if ($null -eq $childSummary -or
                $null -eq $childSummary.PSObject.Properties['appliedClassSubstituteSelections']) {
                @()
            }
            else {
                @($childSummary.appliedClassSubstituteSelections)
            }
            configuredStartupDialogActions = @($case['startupDialogActions'])
            appliedStartupDialogActions = if ($null -eq $childSummary -or
                $null -eq $childSummary.PSObject.Properties['appliedStartupDialogActions']) {
                @()
            }
            else {
                @($childSummary.appliedStartupDialogActions)
            }
            configuredWorldProbes = @($case['worldProbes'])
            worldProbes = if ($null -eq $childSummary -or
                $null -eq $childSummary.PSObject.Properties['worldProbes']) {
                @()
            }
            else {
                @($childSummary.worldProbes)
            }
            expectedPackages = $packages
            passed = [bool]$run['passed']
            classification = if ($null -eq $failureArtifacts) {
                if ([bool]$run['passed']) { "passed" } else { "runner-failure" }
            }
            else {
                [string]$failureArtifacts.classification
            }
            failurePhase = if ($null -eq $failureArtifacts) { "" } else { [string]$failureArtifacts.phase }
            processAliveAtFailure = if ($null -eq $failureArtifacts) { $null } else { [bool]$failureArtifacts.processAlive }
            loadedMods = if ($null -eq $childSummary -or
                $null -eq $childSummary.PSObject.Properties['stagedMods']) {
                @()
            }
            else {
                @($childSummary.stagedMods)
            }
            runtimeLogAudit = $runtimeLogAudit
            error = [string]$run['error']
            summaryPath = [string]$run['summaryPath']
        })
    }

    Write-Host "Starting empty-corpus cleanup verification."
    $cleanupResult = Invoke-DeviceRunner -Name "99-cleanup" -Mode "doctor"
    if (-not [bool]$cleanupResult['passed']) {
        throw "Empty-corpus cleanup verification failed."
    }

    $failedGatedCases = @($caseResults | Where-Object {
        [bool]$_['gate'] -and -not [bool]$_['passed']
    })
    $passed = $failedGatedCases.Count -eq 0
    $errorMessage = if ($passed) {
        ""
    }
    else {
        "Gated mod matrix cases failed: $(@($failedGatedCases | ForEach-Object { $_['id'] }) -join ', ')."
    }
    $summary = New-MatrixSummary -Passed $passed -ErrorMessage $errorMessage
    Write-Utf8Json $summaryPath $summary
    if (-not $passed) {
        throw $errorMessage
    }
    $failedObservationalCases = @($caseResults | Where-Object {
        -not [bool]$_['gate'] -and -not [bool]$_['passed']
    })
    if ($failedObservationalCases.Count -gt 0) {
        Write-Host "ANDROID MOD MATRIX GATE PASSED WITH OBSERVATIONAL FAILURES"
        Write-Host "observationalFailures=$($failedObservationalCases.Count)"
    }
    else {
        Write-Host "ANDROID MOD MATRIX PASSED"
    }
    Write-Host "cases=$($caseResults.Count)"
    Write-Host "summary=$summaryPath"
}
catch {
    $primaryFailure = $_
    if ($null -ne $baselineResult -and $null -eq $cleanupResult) {
        try {
            $cleanupResult = Invoke-DeviceRunner -Name "99-cleanup-after-failure" -Mode "doctor"
        }
        catch {
            $cleanupResult = [ordered]@{
                passed = $false
                error = $_.Exception.ToString()
            }
        }
    }
    $failureSummary = New-MatrixSummary -Passed $false -ErrorMessage $primaryFailure.Exception.ToString()
    Write-Utf8Json $summaryPath $failureSummary
    Write-Host "Android mod matrix failed. Artifacts: $artifactDirectory"
    throw $primaryFailure
}
