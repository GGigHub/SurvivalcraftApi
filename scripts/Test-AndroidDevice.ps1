[CmdletBinding()]
param(
    [ValidateSet("doctor", "relay-reentry-pad")]
    [string]$Mode = "relay-reentry-pad",
    [string]$DeviceId,
    [string]$ApkPath,
    [string]$OutputDirectory,
    [switch]$NoBuild,
    [switch]$NoInstall,
    [switch]$SkipDeviceConfiguration,
    [switch]$PreserveTestData,
    [switch]$KeepTestWorld,
    [ValidateRange(1, 20)]
    [int]$ReentryCount = 3,
    [ValidateRange(10, 180)]
    [int]$BridgeTimeoutSeconds = 90,
    [ValidateRange(10, 180)]
    [int]$WorldTimeoutSeconds = 90,
    [ValidateRange(10, 180)]
    [int]$RelayTimeoutSeconds = 60,
    [ValidateRange(0, 5)]
    [int]$RelayRestartCount = 2,
    [ValidateRange(0, 5)]
    [int]$AdbRetryCount = 2,
    [ValidateRange(0, 1000)]
    [int]$InjectAdbFailureAtSequence = 0,
    [string[]]$ModPackagePath = @(),
    [string[]]$ExpectedModPackageName = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packageName = "com.candy.survivalcraftAPI1_9.autotest"
$remoteDataRoot = "/sdcard/Survivalcraft2.4_API1.9_AutoTest"
$remoteModsRoot = "$remoteDataRoot/Mods"
$remoteBridgeRoot = "$remoteDataRoot/AutoTest"
$buildMetadataPath = Join-Path $repositoryRoot "artifacts\android-device-autotest\android-device-autotest.json"
$runId = "{0:yyyyMMdd-HHmmss}-{1}" -f [DateTime]::Now, $Mode
$testResultsRoot = Join-Path $repositoryRoot "TestResults"
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $testResultsRoot "android-device\$runId"
}
else {
    $candidate = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
        [System.IO.Path]::GetFullPath($OutputDirectory)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
    }
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
$requestDirectory = Join-Path $artifactDirectory "requests"
$responseDirectory = Join-Path $artifactDirectory "responses"
$script:Sequence = 0
$script:StateTimeline = [System.Collections.Generic.List[object]]::new()
$script:AdbRecoveryEvents = [System.Collections.Generic.List[object]]::new()
$script:FaultInjectionEvents = [System.Collections.Generic.List[object]]::new()
$script:RelayRecoveryEvents = [System.Collections.Generic.List[object]]::new()
$script:AdbFailureInjected = $false
$script:DeviceConfiguration = $null
$script:DeviceInteractive = $null
$script:CurrentPhase = "preflight"
$script:LastBridgeCommand = ""
$script:LastBridgeRequestId = ""
$script:StagedModPackages = [System.Collections.Generic.List[object]]::new()
$startedUtc = [DateTimeOffset]::UtcNow
$succeeded = $false
$worldName = ""
$worldDirectory = ""
$relayRuns = [System.Collections.Generic.List[object]]::new()
$adbPath = (Get-Command adb -ErrorAction Stop).Source

function Test-AdbTransportFailure {
    param([int]$ExitCode, [string[]]$Output)
    if ($ExitCode -eq 0) {
        return $false
    }
    $message = $Output -join "`n"
    return $message -match (
        "(?i)(cannot connect to daemon|daemon (?:still )?not running|" +
        "device offline|device not found|no devices/emulators found|" +
        "transport error|connection reset|connection refused|closed)")
}

function Restore-AdbConnection {
    param([string]$FailedCommand, [string[]]$FailureOutput, [int]$Attempt)
    $event = [ordered]@{
        utc = [DateTimeOffset]::UtcNow.ToString("O")
        attempt = $Attempt
        command = $FailedCommand
        failure = ($FailureOutput -join "`n")
        recovered = $false
    }
    $null = @(& $adbPath start-server 2>&1)
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    do {
        Start-Sleep -Milliseconds 500
        $state = @(& $adbPath -s $DeviceId get-state 2>&1)
        if ($LASTEXITCODE -eq 0 -and ($state -join "").Trim() -eq "device") {
            $event.recovered = $true
            $script:AdbRecoveryEvents.Add($event)
            return $true
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    $script:AdbRecoveryEvents.Add($event)
    return $false
}

function Invoke-AdbRaw {
    param([string[]]$Arguments)
    for ($attempt = 0; $attempt -le $AdbRetryCount; $attempt++) {
        $shouldInject = $attempt -eq 0 `
            -and -not $script:AdbFailureInjected `
            -and $InjectAdbFailureAtSequence -gt 0 `
            -and $script:Sequence -eq $InjectAdbFailureAtSequence `
            -and $Arguments.Count -gt 0 `
            -and $Arguments[0] -eq "push"
        if ($shouldInject) {
            $script:AdbFailureInjected = $true
            $script:FaultInjectionEvents.Add([ordered]@{
                utc = [DateTimeOffset]::UtcNow.ToString("O")
                kind = "adb-daemon-stop"
                sequence = $script:Sequence
                command = $Arguments -join " "
            })
            $null = @(& $adbPath kill-server 2>&1)
            $output = @("[injected] cannot connect to daemon")
            $exitCode = 1
        }
        else {
            $output = @(& $adbPath -s $DeviceId @Arguments 2>&1)
            $exitCode = $LASTEXITCODE
        }
        if ($exitCode -eq 0 -or -not (Test-AdbTransportFailure $exitCode $output)) {
            return [pscustomobject]@{
                ExitCode = $exitCode
                Output = $output
            }
        }
        if ($attempt -ge $AdbRetryCount -or -not (
            Restore-AdbConnection ($Arguments -join " ") $output ($attempt + 1))) {
            return [pscustomobject]@{
                ExitCode = $exitCode
                Output = $output
            }
        }
    }
    throw "Unreachable ADB retry state."
}

function Invoke-Adb {
    param([string[]]$Arguments)
    $result = Invoke-AdbRaw $Arguments
    if ($result.ExitCode -ne 0) {
        throw "adb $($Arguments -join ' ') failed with code $($result.ExitCode): $($result.Output -join [Environment]::NewLine)"
    }
    return $result.Output
}

function Resolve-Device {
    if (-not [string]::IsNullOrWhiteSpace($DeviceId)) {
        $matching = @(& $adbPath devices | Where-Object { $_ -match "^$([regex]::Escape($DeviceId))\s+device$" })
        if ($matching.Count -ne 1) {
            throw "Android device '$DeviceId' is not connected and authorized."
        }
        return
    }
    $connected = @(& $adbPath devices | ForEach-Object {
        if ($_ -match "^([^\s]+)\s+device$") { $Matches[1] }
    })
    if ($connected.Count -ne 1) {
        throw "Expected exactly one connected Android device, found $($connected.Count). Pass -DeviceId explicitly."
    }
    $script:DeviceId = $connected[0]
}

function Get-AndroidSetting {
    param([string]$Namespace, [string]$Key)
    return (@(Invoke-Adb @("shell", "settings", "get", $Namespace, $Key)) -join "").Trim()
}

function Get-DeviceInteractiveSnapshot {
    $power = Invoke-Adb @("shell", "dumpsys", "power")
    $policy = Invoke-Adb @("shell", "dumpsys", "window", "policy")
    $powerText = $power -join "`n"
    $policyText = $policy -join "`n"
    $awake = $powerText -match "(?m)^\s*mWakefulness=Awake\s*$"
    $hasInteractiveState = $policyText -match "INTERACTIVE_STATE_"
    return [ordered]@{
        awake = $awake
        keyguardShowing = $policyText -match "(?m)^\s+showing=true\s*$"
        interactive = if ($hasInteractiveState) {
            $policyText -match "INTERACTIVE_STATE_AWAKE"
        }
        else {
            $awake -or $policyText -match "(?m)^\s*mAwake=true\s*$"
        }
    }
}

function Enable-UnattendedDeviceSession {
    $before = Get-DeviceInteractiveSnapshot
    $stayAwakeBefore = Get-AndroidSetting "global" "stay_on_while_plugged_in"
    $session = [ordered]@{
        before = $before
        after = $null
        stayAwakeBefore = $stayAwakeBefore
        stayAwakeDuring = ""
        restored = $false
        restoreError = ""
    }
    $script:DeviceInteractive = $session
    Invoke-Adb @("shell", "svc", "power", "stayon", "usb") | Out-Null
    Invoke-Adb @("shell", "input", "keyevent", "KEYCODE_WAKEUP") | Out-Null
    $null = Invoke-AdbRaw @("shell", "wm", "dismiss-keyguard")

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $after = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        $after = Get-DeviceInteractiveSnapshot
        if ($after['awake'] -and $after['interactive'] -and -not $after['keyguardShowing']) {
            break
        }
        $null = Invoke-AdbRaw @("shell", "input", "keyevent", "KEYCODE_WAKEUP")
        $null = Invoke-AdbRaw @("shell", "wm", "dismiss-keyguard")
        Start-Sleep -Milliseconds 500
    }
    if ($null -eq $after -or -not $after['awake'] -or
        -not $after['interactive'] -or $after['keyguardShowing']) {
        throw "Android device could not be made interactive without credentials; unlock the dedicated test device once and retry."
    }
    $session['after'] = $after
    $session['stayAwakeDuring'] = Get-AndroidSetting "global" "stay_on_while_plugged_in"
    return $session
}

function Restore-UnattendedDeviceSession {
    if ($null -eq $script:DeviceInteractive) {
        return
    }
    try {
        $originalStayAwake = [string]$script:DeviceInteractive['stayAwakeBefore']
        if ($originalStayAwake -match "^\d+$") {
            $restoreSetting = Invoke-AdbRaw @(
                "shell", "settings", "put", "global", "stay_on_while_plugged_in", $originalStayAwake)
        }
        else {
            $restoreSetting = Invoke-AdbRaw @(
                "shell", "settings", "delete", "global", "stay_on_while_plugged_in")
        }
        if ($restoreSetting.ExitCode -ne 0) {
            throw "Could not restore stay_on_while_plugged_in: $($restoreSetting.Output -join ' ')"
        }
        $restoredStayAwake = Get-AndroidSetting "global" "stay_on_while_plugged_in"
        if ($originalStayAwake -match "^\d+$" -and $restoredStayAwake -ne $originalStayAwake) {
            throw "stay_on_while_plugged_in restored to '$restoredStayAwake', expected '$originalStayAwake'."
        }
        if (-not [bool]$script:DeviceInteractive['before']['awake']) {
            $sleepResult = Invoke-AdbRaw @("shell", "input", "keyevent", "KEYCODE_SLEEP")
            if ($sleepResult.ExitCode -ne 0) {
                throw "Could not restore the device sleep state: $($sleepResult.Output -join ' ')"
            }
        }
        $script:DeviceInteractive['restored'] = $true
    }
    catch {
        $script:DeviceInteractive['restoreError'] = $_.Exception.Message
    }
}

function Get-AndroidUiHierarchy {
    param([string]$Name)
    $remotePath = "/sdcard/survivalcraft-autotest-$Name.xml"
    Invoke-Adb @("shell", "uiautomator", "dump", $remotePath) | Out-Null
    $raw = @(Invoke-Adb @("shell", "cat", $remotePath)) -join "`n"
    return [xml]$raw
}

function Get-UiNodeCenter {
    param([System.Xml.XmlElement]$Node)
    $bounds = [string]$Node.bounds
    if ($bounds -notmatch "^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$") {
        throw "Android UI node has invalid bounds '$bounds'."
    }
    return [pscustomobject]@{
        X = [int](([int]$Matches[1] + [int]$Matches[3]) / 2)
        Y = [int](([int]$Matches[2] + [int]$Matches[4]) / 2)
    }
}

function Invoke-UiNodeTap {
    param([System.Xml.XmlElement]$Node)
    $center = Get-UiNodeCenter $Node
    Invoke-Adb @("shell", "input", "tap", [string]$center.X, [string]$center.Y) | Out-Null
}

function Set-AdbInstallConfirmationDisabled {
    $before = Get-AndroidSetting "secure" "adb_install_need_confirm"
    $verifierBefore = Get-AndroidSetting "global" "package_verifier_enable"
    $result = [ordered]@{
        setting = "secure/adb_install_need_confirm"
        before = $before
        after = $before
        changed = $false
        method = "none"
        packageVerifierBefore = $verifierBefore
        packageVerifierAfter = $verifierBefore
    }
    if ($before -ne "1") {
        return $result
    }
    if ($SkipDeviceConfiguration) {
        throw "Device requires confirmation for ADB installs. Re-run without -SkipDeviceConfiguration or disable the corresponding developer option."
    }

    Invoke-Adb @("shell", "input", "keyevent", "KEYCODE_WAKEUP") | Out-Null
    $null = Invoke-AdbRaw @("shell", "wm", "dismiss-keyguard")
    $null = Invoke-AdbRaw @("shell", "am", "force-stop", "com.android.settings")
    try {
        Invoke-Adb @(
            "shell", "am", "start", "-W", "-a",
            "android.settings.APPLICATION_DEVELOPMENT_SETTINGS") | Out-Null
        Start-Sleep -Milliseconds 800
        $target = $null
        $switch = $null
        for ($page = 0; $page -lt 12; $page++) {
            $ui = Get-AndroidUiHierarchy "developer-options-$page"
            $target = $ui.SelectNodes("//node[@text != '']") | Where-Object {
                $_.text -eq "使用 ADB 安装应用时，需要用户确认" -or
                $_.text -match "(?i)(ADB.*install.*(?:confirm|confirmation|user)|confirm.*ADB.*install)"
            } | Select-Object -First 1
            if ($null -ne $target) {
                for ($ancestor = $target; $null -ne $ancestor; $ancestor = $ancestor.ParentNode) {
                    $switch = $ancestor.SelectSingleNode(
                        ".//node[@resource-id='android:id/switch_widget' and @checkable='true']")
                    if ($null -ne $switch) {
                        break
                    }
                }
                if ($null -ne $switch) {
                    break
                }
            }
            $scroll = $ui.SelectSingleNode("//node[@scrollable='true']")
            if ($null -eq $scroll) {
                break
            }
            $bounds = [string]$scroll.bounds
            if ($bounds -notmatch "^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$") {
                throw "Developer options list has invalid bounds '$bounds'."
            }
            $x = [int](([int]$Matches[1] + [int]$Matches[3]) / 2)
            $height = [int]$Matches[4] - [int]$Matches[2]
            $startY = [int]([int]$Matches[2] + $height * 0.82)
            $endY = [int]([int]$Matches[2] + $height * 0.18)
            Invoke-Adb @(
                "shell", "input", "swipe", [string]$x, [string]$startY,
                [string]$x, [string]$endY, "350") | Out-Null
            Start-Sleep -Milliseconds 400
        }
        if ($null -eq $switch) {
            throw "Could not locate the ADB install-confirmation switch in Android developer options."
        }
        if ($switch.checked -ne "true") {
            throw "ADB install-confirmation setting is 1 but its developer-options switch is not checked."
        }
        Invoke-UiNodeTap $switch
        Start-Sleep -Milliseconds 800
        $after = Get-AndroidSetting "secure" "adb_install_need_confirm"
        if ($after -ne "0") {
            $confirmationUi = Get-AndroidUiHierarchy "developer-options-confirmation"
            $positive = $confirmationUi.SelectSingleNode(
                "//node[@resource-id='android:id/button1' and @enabled='true']")
            if ($null -ne $positive) {
                Invoke-UiNodeTap $positive
                Start-Sleep -Milliseconds 800
                $after = Get-AndroidSetting "secure" "adb_install_need_confirm"
            }
        }
        if ($after -ne "0") {
            throw "Developer option did not disable ADB install confirmation (value='$after')."
        }
        $verifierAfter = Get-AndroidSetting "global" "package_verifier_enable"
        if ($verifierAfter -ne $verifierBefore) {
            throw "Package verifier changed unexpectedly from '$verifierBefore' to '$verifierAfter'."
        }
        $result.after = $after
        $result.changed = $true
        $result.method = "semantic-developer-options"
        $result.packageVerifierAfter = $verifierAfter
        return $result
    }
    finally {
        $null = Invoke-AdbRaw @("shell", "am", "force-stop", "com.android.settings")
    }
}

function Write-Utf8Json {
    param([string]$Path, [object]$Value)
    [System.IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Resolve-ModPackageInputs {
    $resolvedPaths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $expectedNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)

    foreach ($expectedNameValue in @($ExpectedModPackageName)) {
        $expectedName = ([string]$expectedNameValue).Trim()
        if ([string]::IsNullOrWhiteSpace($expectedName)) {
            throw "ExpectedModPackageName cannot contain an empty value."
        }
        if (-not $expectedNames.Add($expectedName)) {
            throw "Expected mod package name '$expectedName' was specified more than once."
        }
    }

    $index = 0
    foreach ($pathValue in @($ModPackagePath)) {
        if ([string]::IsNullOrWhiteSpace([string]$pathValue)) {
            throw "ModPackagePath cannot contain an empty value."
        }
        $resolvedPath = (Resolve-Path -LiteralPath ([string]$pathValue) -ErrorAction Stop).Path
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
            throw "Mod package '$resolvedPath' is not a file."
        }
        if ([System.IO.Path]::GetExtension($resolvedPath) -ine ".scmod") {
            throw "Mod package '$resolvedPath' must use the .scmod extension."
        }
        if (-not $resolvedPaths.Add($resolvedPath)) {
            throw "Mod package '$resolvedPath' was specified more than once."
        }
        $file = Get-Item -LiteralPath $resolvedPath
        if ($file.Length -le 0 -or $file.Length -gt 64MB) {
            throw "Mod package '$resolvedPath' has invalid size $($file.Length); allowed range is 1 byte through 64 MiB."
        }
        $index++
        $remoteFileName = "corpus-{0:D3}.scmod" -f $index
        $script:StagedModPackages.Add([ordered]@{
            localPath = $resolvedPath
            sourceFileName = $file.Name
            remoteFileName = $remoteFileName
            remotePath = "$remoteModsRoot/$remoteFileName"
            bytes = $file.Length
            sha256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    if ($script:StagedModPackages.Count -eq 0 -and $expectedNames.Count -gt 0) {
        throw "ExpectedModPackageName requires at least one ModPackagePath."
    }
    if ($script:StagedModPackages.Count -gt 0 -and $expectedNames.Count -ne $script:StagedModPackages.Count) {
        throw "Expected exactly one ExpectedModPackageName per staged .scmod file; got $($expectedNames.Count) name(s) for $($script:StagedModPackages.Count) file(s)."
    }
    return @($expectedNames)
}

function Assert-ModInventory {
    param([object]$ModsResponse, [string]$Phase)

    $allMods = @($ModsResponse.data.mods)
    $stagedFileNames = @($script:StagedModPackages | ForEach-Object { [string]$_.remoteFileName })
    $stagedMods = @($allMods | Where-Object { $stagedFileNames -contains [string]$_.fileName })
    if ($stagedMods.Count -ne $script:StagedModPackages.Count) {
        $seen = @($stagedMods | ForEach-Object { [string]$_.fileName }) -join ", "
        throw "Mod inventory '$Phase' reported $($stagedMods.Count) of $($script:StagedModPackages.Count) staged file(s). Seen: [$seen]."
    }
    foreach ($mod in $stagedMods) {
        if ($mod.disabled) {
            throw "Staged mod '$($mod.packageName)' from '$($mod.fileName)' is disabled: $($mod.disableReason)."
        }
    }
    foreach ($expectedName in @($ExpectedModPackageName)) {
        $matches = @($allMods | Where-Object {
            [string]::Equals(
                [string]$_.packageName,
                ([string]$expectedName).Trim(),
                [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($matches.Count -ne 1) {
            throw "Expected exactly one mod package '$expectedName' during '$Phase', found $($matches.Count)."
        }
        if ($matches[0].disabled) {
            throw "Expected mod package '$expectedName' is disabled during '$Phase': $($matches[0].disableReason)."
        }
    }
    return $stagedMods
}

function Send-BridgeCommand {
    param(
        [string]$Command,
        [object]$Arguments = $null,
        [int]$TimeoutSeconds = 20
    )
    $script:Sequence++
    $id = "{0:D6}-{1}" -f $script:Sequence, ([Guid]::NewGuid().ToString("N"))
    $script:LastBridgeCommand = $Command
    $script:LastBridgeRequestId = $id
    $request = [ordered]@{
        schemaVersion = 1
        id = $id
        command = $Command
        arguments = if ($null -eq $Arguments) { [ordered]@{} } else { $Arguments }
    }
    $localRequest = Join-Path $requestDirectory "$id.json"
    $localResponse = Join-Path $responseDirectory "$id.json"
    $remoteTemporary = "$remoteBridgeRoot/inbox/$id.tmp"
    $remoteRequest = "$remoteBridgeRoot/inbox/$id.json"
    $remoteProcessed = "$remoteBridgeRoot/processed/$id.json"
    $remoteResponse = "$remoteBridgeRoot/responses/$id.json"
    Write-Utf8Json $localRequest $request
    Invoke-Adb @("push", $localRequest, $remoteTemporary) | Out-Null
    $publishCommand = "if [ -f '$remoteTemporary' ]; then mv '$remoteTemporary' '$remoteRequest'; " +
        "elif [ -f '$remoteRequest' ] || [ -f '$remoteProcessed' ] || [ -f '$remoteResponse' ]; " +
        "then exit 0; else exit 2; fi"
    Invoke-Adb @("shell", $publishCommand) | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $exists = Invoke-AdbRaw @("shell", "ls", $remoteResponse)
        if ($exists.ExitCode -eq 0) {
            Invoke-Adb @("pull", $remoteResponse, $localResponse) | Out-Null
            $response = Get-Content -LiteralPath $localResponse -Raw | ConvertFrom-Json
            if (-not $response.ok) {
                $message = if ($null -ne $response.error) {
                    "$($response.error.type): $($response.error.message)"
                }
                else {
                    "Unknown bridge error."
                }
                throw "Android bridge command '$Command' failed: $message"
            }
            return $response
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out after $TimeoutSeconds seconds waiting for Android bridge command '$Command' ($id)."
}

function Add-StateObservation {
    param([object]$State, [string]$Phase)
    $script:StateTimeline.Add([ordered]@{
        utc = [DateTimeOffset]::UtcNow.ToString("O")
        phase = $Phase
        screen = $State.screen
        dialog = $State.dialog
        projectLoaded = $State.projectLoaded
        playerReady = $State.playerReady
        moveControlMode = $State.moveControlMode
        serverRunning = $State.serverRunning
        relayStatus = $State.relayStatus
        relayGlobalCode = $State.relayGlobalCode
        relayRoomCode = $State.relayRoomCode
        relayEndPoint = $State.relayEndPoint
        relayAttemptCount = $State.relayAttemptCount
        relayLastFailure = $State.relayLastFailure
    })
}

function Get-BridgeState {
    param([string]$Phase = "poll")
    $state = (Send-BridgeCommand "state.get").data
    Add-StateObservation $state $Phase
    return $state
}

function Wait-ForPlayableWorld {
    param([string]$Phase)
    $deadline = [DateTime]::UtcNow.AddSeconds($WorldTimeoutSeconds)
    $playerEnterSent = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $state = Get-BridgeState $Phase
        if ($state.screen -eq "PlayerScreen" -and -not $playerEnterSent) {
            Send-BridgeCommand "player.enter" ([ordered]@{ name = "AndroidAutoTest" }) | Out-Null
            $playerEnterSent = $true
        }
        elseif ($state.screen -eq "GameScreen" -and $state.projectLoaded) {
            return $state
        }
        Start-Sleep -Milliseconds 750
    }
    throw "Timed out waiting for a playable world during '$Phase'."
}

function Wait-ForRelayCode {
    param([string]$Phase)
    $deadline = [DateTime]::UtcNow.AddSeconds($RelayTimeoutSeconds)
    $restartCount = 0
    while ([DateTime]::UtcNow -lt $deadline) {
        $state = Get-BridgeState $Phase
        $code = if (-not [string]::IsNullOrWhiteSpace([string]$state.relayGlobalCode)) {
            [string]$state.relayGlobalCode
        }
        else {
            [string]$state.relayRoomCode
        }
        if ($state.relayStatus -eq "Ready" -and -not [string]::IsNullOrWhiteSpace($code)) {
            return [pscustomobject]@{
                State = $state
                Code = $code
                RestartCount = $restartCount
            }
        }
        if ($state.relayStatus -eq "Failed") {
            if ($restartCount -ge $RelayRestartCount) {
                throw "Relay creation failed during '$Phase' after $restartCount restart(s). " +
                    "Endpoint='$($state.relayEndPoint)', attempts=$($state.relayAttemptCount), " +
                    "lastFailure='$($state.relayLastFailure)'."
            }
            $restartCount++
            $script:RelayRecoveryEvents.Add([ordered]@{
                utc = [DateTimeOffset]::UtcNow.ToString("O")
                phase = $Phase
                restart = $restartCount
                endPoint = $state.relayEndPoint
                attemptCount = $state.relayAttemptCount
                lastFailure = $state.relayLastFailure
            })
            Start-Sleep -Milliseconds 500
            Send-BridgeCommand "network.startRelayHost" | Out-Null
        }
        Start-Sleep -Milliseconds 750
    }
    throw "Timed out waiting for a relay code during '$Phase'."
}

function Wait-ForMainMenu {
    param([string]$Phase)
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $state = Get-BridgeState $Phase
        if ($state.screen -eq "MainMenuScreen" -and -not $state.projectLoaded) {
            return $state
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for the main menu during '$Phase'."
}

function Capture-FailureArtifacts {
    $logcat = Invoke-AdbRaw @("logcat", "-d", "-v", "threadtime")
    $logcatPath = Join-Path $artifactDirectory "failure-logcat.txt"
    [System.IO.File]::WriteAllLines(
        $logcatPath,
        [string[]]$logcat.Output,
        [System.Text.UTF8Encoding]::new($false))
    $exitInfo = Invoke-AdbRaw @("shell", "dumpsys", "activity", "exit-info", $packageName)
    $exitInfoPath = Join-Path $artifactDirectory "failure-exit-info.txt"
    [System.IO.File]::WriteAllLines(
        $exitInfoPath,
        [string[]]$exitInfo.Output,
        [System.Text.UTF8Encoding]::new($false))
    $pidResult = Invoke-AdbRaw @("shell", "pidof", $packageName)
    $processAlive = $pidResult.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace(
        ($pidResult.Output -join "").Trim())
    $markerPattern = "(?i)(FATAL EXCEPTION|Fatal signal|ANR in|AndroidRuntime|" +
        [regex]::Escape($packageName) + ")"
    $crashMarkers = @($logcat.Output | Where-Object { $_ -match $markerPattern } | Select-Object -Last 120)
    $modTokens = @($script:StagedModPackages | ForEach-Object {
        [System.IO.Path]::GetFileNameWithoutExtension([string]$_.sourceFileName)
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $modMarkerLines = @()
    foreach ($line in @($logcat.Output)) {
        foreach ($token in $modTokens) {
            if ([string]$line -match [regex]::Escape($token)) {
                $modMarkerLines += [string]$line
                break
            }
        }
    }
    $classification = if (-not $processAlive -and $script:CurrentPhase -match "startup|bridge|mod-inventory") {
        "process-exited-during-startup"
    }
    elseif (-not $processAlive) {
        "process-exited-during-test"
    }
    elseif ($script:CurrentPhase -match "mod-inventory") {
        "mod-inventory-rejected"
    }
    elseif ($script:LastBridgeCommand -ne "" -and $script:CurrentPhase -match "bridge|startup") {
        "automation-bridge-unavailable"
    }
    else {
        "test-assertion-or-timeout"
    }
    $remoteScreenshot = "$remoteBridgeRoot/failure-$runId.png"
    $capture = Invoke-AdbRaw @("shell", "screencap", "-p", $remoteScreenshot)
    $screenshotPath = ""
    if ($capture.ExitCode -eq 0) {
        $screenshotPath = Join-Path $artifactDirectory "failure-screen.png"
        $null = Invoke-AdbRaw @(
            "pull",
            $remoteScreenshot,
            $screenshotPath)
    }
    Write-Utf8Json (Join-Path $artifactDirectory "state-timeline.json") $script:StateTimeline
    return [ordered]@{
        classification = $classification
        phase = $script:CurrentPhase
        processAlive = $processAlive
        lastBridgeCommand = $script:LastBridgeCommand
        lastBridgeRequestId = $script:LastBridgeRequestId
        logcatPath = $logcatPath
        exitInfoPath = $exitInfoPath
        screenshotPath = $screenshotPath
        crashMarkers = $crashMarkers
        modMarkerLines = @($modMarkerLines | Select-Object -Last 120)
    }
}

if ($remoteDataRoot -ne "/sdcard/Survivalcraft2.4_API1.9_AutoTest") {
    throw "Refusing to operate on unexpected Android data root '$remoteDataRoot'."
}

New-Item -ItemType Directory -Path $requestDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $responseDirectory -Force | Out-Null
$null = Resolve-ModPackageInputs
Resolve-Device

$deviceModel = (@(Invoke-Adb @("shell", "getprop", "ro.product.model")) -join "").Trim()
$deviceAbi = (@(Invoke-Adb @("shell", "getprop", "ro.product.cpu.abilist")) -join "").Trim()
$androidVersion = (@(Invoke-Adb @("shell", "getprop", "ro.build.version.release")) -join "").Trim()
if ($deviceAbi -notmatch "arm64-v8a") {
    throw "Device '$DeviceId' does not advertise arm64-v8a (abilist='$deviceAbi')."
}

if (!$NoBuild) {
    & (Join-Path $PSScriptRoot "Build-AndroidDeviceTest.ps1")
}
if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    if (-not (Test-Path -LiteralPath $buildMetadataPath -PathType Leaf)) {
        throw "Build metadata not found at '$buildMetadataPath'. Run without -NoBuild first."
    }
    $buildMetadata = Get-Content -LiteralPath $buildMetadataPath -Raw | ConvertFrom-Json
    $ApkPath = [string]$buildMetadata.apk
}
$ApkPath = (Resolve-Path -LiteralPath $ApkPath).Path

try {
    $script:CurrentPhase = "device-interactive"
    $script:DeviceInteractive = Enable-UnattendedDeviceSession
    $script:CurrentPhase = "install"
    if (!$NoInstall) {
        $script:DeviceConfiguration = Set-AdbInstallConfirmationDisabled
    }
    else {
        $script:DeviceConfiguration = [ordered]@{
            setting = "secure/adb_install_need_confirm"
            before = Get-AndroidSetting "secure" "adb_install_need_confirm"
            after = Get-AndroidSetting "secure" "adb_install_need_confirm"
            changed = $false
            method = "not-required-no-install"
            packageVerifierBefore = Get-AndroidSetting "global" "package_verifier_enable"
            packageVerifierAfter = Get-AndroidSetting "global" "package_verifier_enable"
        }
    }
    if (!$NoInstall) {
        Invoke-Adb @("install", "-r", "-t", "-g", $ApkPath) | Out-Null
    }
    if (!$PreserveTestData) {
        $null = Invoke-AdbRaw @("shell", "am", "force-stop", $packageName)
        $null = Invoke-AdbRaw @("shell", "pm", "clear", $packageName)
        Invoke-Adb @("shell", "rm", "-rf", $remoteDataRoot) | Out-Null
    }

    $script:CurrentPhase = "stage-mods"
    $null = Invoke-AdbRaw @("shell", "pm", "grant", $packageName, "android.permission.READ_EXTERNAL_STORAGE")
    $null = Invoke-AdbRaw @("shell", "pm", "grant", $packageName, "android.permission.WRITE_EXTERNAL_STORAGE")
    $null = Invoke-AdbRaw @("shell", "appops", "set", $packageName, "MANAGE_EXTERNAL_STORAGE", "allow")
    Invoke-Adb @("shell", "rm", "-rf", $remoteModsRoot) | Out-Null
    Invoke-Adb @("shell", "mkdir", "-p", $remoteModsRoot) | Out-Null
    foreach ($modPackage in $script:StagedModPackages) {
        Invoke-Adb @("push", [string]$modPackage.localPath, [string]$modPackage.remotePath) | Out-Null
    }
    Invoke-Adb @("shell", "mkdir", "-p", "$remoteBridgeRoot/inbox") | Out-Null
    Invoke-Adb @("shell", "mkdir", "-p", "$remoteBridgeRoot/responses") | Out-Null
    Invoke-Adb @("shell", "mkdir", "-p", "$remoteBridgeRoot/processed") | Out-Null
    $null = Invoke-AdbRaw @("logcat", "-c")

    $resolvedActivityOutput = Invoke-Adb @("shell", "cmd", "package", "resolve-activity", "--brief", $packageName)
    $component = @($resolvedActivityOutput | Where-Object { $_ -match "/" } | Select-Object -Last 1)
    if ($component.Count -ne 1) {
        throw "Could not resolve launcher activity for '$packageName': $($resolvedActivityOutput -join ' ')"
    }
    $script:CurrentPhase = "startup-bridge"
    Invoke-Adb @("shell", "am", "start", "-W", "-n", $component[0].Trim()) | Out-Null

    $ping = Send-BridgeCommand "system.ping" $null $BridgeTimeoutSeconds
    $script:CurrentPhase = "mod-inventory-startup"
    $mods = Send-BridgeCommand "mods.list"
    $testMod = @($mods.data.mods | Where-Object {
        $_.packageName -eq "Survivalcraft.AndroidTestAutomation" -and -not $_.disabled
    })
    if ($testMod.Count -ne 1) {
        throw "Android device test automation mod did not report exactly one enabled instance."
    }
    $multiplayerMod = @($mods.data.mods | Where-Object {
        $_.packageName -eq "network" -and -not $_.disabled
    })
    if ($multiplayerMod.Count -ne 1) {
        throw "Multiplayer mod did not report exactly one enabled instance."
    }
    $stagedStartupMods = Assert-ModInventory $mods "startup"
    $script:CurrentPhase = "main-menu-startup"
    $null = Wait-ForMainMenu "startup"

    if ($Mode -eq "doctor") {
        $script:CurrentPhase = "doctor"
        $state = Get-BridgeState "doctor"
        $tree = Send-BridgeCommand "ui.tree"
        if ($InjectAdbFailureAtSequence -gt 0 -and -not $script:AdbFailureInjected) {
            throw "Requested ADB fault injection at sequence $InjectAdbFailureAtSequence was not reached."
        }
        $summary = [ordered]@{
            schemaVersion = 1
            mode = $Mode
            passed = $true
            startedUtc = $startedUtc.ToString("O")
            completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
            device = [ordered]@{
                id = $DeviceId
                model = $deviceModel
                androidVersion = $androidVersion
                abiList = $deviceAbi
            }
            apk = $ApkPath
            bridge = $ping.data
            adbRecoveryEvents = $script:AdbRecoveryEvents
            faultInjectionEvents = $script:FaultInjectionEvents
            relayRecoveryEvents = $script:RelayRecoveryEvents
            deviceConfiguration = $script:DeviceConfiguration
            deviceInteractive = $script:DeviceInteractive
            state = $state
            mods = $mods.data.mods
            stagedModPackages = $script:StagedModPackages
            expectedModPackageNames = @($ExpectedModPackageName)
            stagedMods = $stagedStartupMods
            visibleWidgetCount = $tree.data.widgets.Count
            artifactDirectory = $artifactDirectory
        }
        Write-Utf8Json (Join-Path $artifactDirectory "summary.json") $summary
        Write-Utf8Json (Join-Path $artifactDirectory "state-timeline.json") $script:StateTimeline
        $succeeded = $true
        Write-Host "ANDROID DEVICE DOCTOR PASSED"
        Write-Host "artifacts=$artifactDirectory"
        return
    }

    $script:CurrentPhase = "relay-settings"
    Send-BridgeCommand "settings.set" ([ordered]@{
        moveControlMode = "Pad"
        playWithFriendsEnabled = $true
        showPlayWithFriendsDialog = $false
        save = $true
    }) | Out-Null

    $script:CurrentPhase = "new-world"
    $worldName = "AutoRelayPad-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
    $create = Send-BridgeCommand "world.create" ([ordered]@{
        name = $worldName
        seed = "android-relay-pad-regression"
        creative = $false
        flatTerrain = $true
        playWithFriends = $true
    })
    $worldDirectory = [string]$create.data.directoryName
    $null = Wait-ForPlayableWorld "new-world"
    $firstRelay = Wait-ForRelayCode "new-world-relay"
    $relayRuns.Add([ordered]@{
        iteration = 0
        kind = "new-world"
        code = $firstRelay.Code
        roomCode = $firstRelay.State.relayRoomCode
        moveControlMode = $firstRelay.State.moveControlMode
        relayEndPoint = $firstRelay.State.relayEndPoint
        relayAttemptCount = $firstRelay.State.relayAttemptCount
        relayLastFailure = $firstRelay.State.relayLastFailure
        restartCount = $firstRelay.RestartCount
    })

    for ($iteration = 1; $iteration -le $ReentryCount; $iteration++) {
        $script:CurrentPhase = "reentry-$iteration"
        Send-BridgeCommand "world.exit" ([ordered]@{ save = $true }) | Out-Null
        $null = Wait-ForMainMenu "exit-$iteration"
        Send-BridgeCommand "world.play" ([ordered]@{
            directoryName = $worldDirectory
            playWithFriends = $true
            saveCurrent = $false
        }) | Out-Null
        $null = Wait-ForPlayableWorld "reentry-$iteration"
        $relay = Wait-ForRelayCode "reentry-$iteration-relay"
        if ($relay.State.moveControlMode -ne "Pad") {
            throw "MoveControlMode drifted to '$($relay.State.moveControlMode)' during reentry $iteration."
        }
        $relayRuns.Add([ordered]@{
            iteration = $iteration
            kind = "existing-world"
            code = $relay.Code
            roomCode = $relay.State.relayRoomCode
            moveControlMode = $relay.State.moveControlMode
            relayEndPoint = $relay.State.relayEndPoint
            relayAttemptCount = $relay.State.relayAttemptCount
            relayLastFailure = $relay.State.relayLastFailure
            restartCount = $relay.RestartCount
        })
    }

    $script:CurrentPhase = "final-cleanup"
    Send-BridgeCommand "world.exit" ([ordered]@{ save = $true }) | Out-Null
    $null = Wait-ForMainMenu "final-exit"
    if (!$KeepTestWorld) {
        Send-BridgeCommand "world.delete" ([ordered]@{ directoryName = $worldDirectory }) | Out-Null
    }
    $finalState = Get-BridgeState "complete"
    $finalModsResponse = Send-BridgeCommand "mods.list"
    $finalMods = $finalModsResponse.data.mods
    $stagedFinalMods = Assert-ModInventory $finalModsResponse "complete"
    if ($InjectAdbFailureAtSequence -gt 0 -and -not $script:AdbFailureInjected) {
        throw "Requested ADB fault injection at sequence $InjectAdbFailureAtSequence was not reached."
    }
    $summary = [ordered]@{
        schemaVersion = 1
        mode = $Mode
        passed = $true
        startedUtc = $startedUtc.ToString("O")
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        device = [ordered]@{
            id = $DeviceId
            model = $deviceModel
            androidVersion = $androidVersion
            abiList = $deviceAbi
        }
        apk = $ApkPath
        apkSha256 = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash.ToLowerInvariant()
        bridgeProtocolVersion = $ping.data.protocolVersion
        adbRecoveryEvents = $script:AdbRecoveryEvents
        faultInjectionEvents = $script:FaultInjectionEvents
        relayRecoveryEvents = $script:RelayRecoveryEvents
        deviceConfiguration = $script:DeviceConfiguration
        deviceInteractive = $script:DeviceInteractive
        worldName = $worldName
        worldDirectory = $worldDirectory
        worldRetained = [bool]$KeepTestWorld
        relayRuns = $relayRuns
        finalState = $finalState
        mods = $finalMods
        stagedModPackages = $script:StagedModPackages
        expectedModPackageNames = @($ExpectedModPackageName)
        stagedMods = $stagedFinalMods
        stateObservationCount = $script:StateTimeline.Count
        artifactDirectory = $artifactDirectory
    }
    Write-Utf8Json (Join-Path $artifactDirectory "summary.json") $summary
    Write-Utf8Json (Join-Path $artifactDirectory "state-timeline.json") $script:StateTimeline
    $succeeded = $true
    Write-Host "ANDROID RELAY REENTRY PAD REGRESSION PASSED"
    Write-Host "runs=$($relayRuns.Count)"
    Write-Host "artifacts=$artifactDirectory"
}
catch {
    $failureException = $_.Exception
    $failureArtifacts = Capture-FailureArtifacts
    $cleanup = [ordered]@{
        attempted = -not [string]::IsNullOrWhiteSpace($worldDirectory)
        succeeded = $false
        error = ""
    }
    if ($cleanup.attempted) {
        try {
            $cleanupState = Get-BridgeState "failure-cleanup"
            if ($cleanupState.projectLoaded) {
                Send-BridgeCommand "world.exit" ([ordered]@{ save = $false }) 10 | Out-Null
                $null = Wait-ForMainMenu "failure-cleanup-exit"
            }
            Send-BridgeCommand "world.delete" ([ordered]@{
                directoryName = $worldDirectory
            }) 10 | Out-Null
            $cleanup.succeeded = $true
        }
        catch {
            $cleanup.error = $_.Exception.Message
        }
    }
    $failure = [ordered]@{
        schemaVersion = 1
        mode = $Mode
        passed = $false
        startedUtc = $startedUtc.ToString("O")
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        deviceId = $DeviceId
        apk = $ApkPath
        adbRecoveryEvents = $script:AdbRecoveryEvents
        faultInjectionEvents = $script:FaultInjectionEvents
        relayRecoveryEvents = $script:RelayRecoveryEvents
        deviceConfiguration = $script:DeviceConfiguration
        deviceInteractive = $script:DeviceInteractive
        worldName = $worldName
        worldDirectory = $worldDirectory
        relayRuns = $relayRuns
        stagedModPackages = $script:StagedModPackages
        expectedModPackageNames = @($ExpectedModPackageName)
        failureArtifacts = $failureArtifacts
        stateObservationCount = $script:StateTimeline.Count
        cleanup = $cleanup
        error = $failureException.ToString()
        artifactDirectory = $artifactDirectory
    }
    Write-Utf8Json (Join-Path $artifactDirectory "summary.json") $failure
    throw
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($DeviceId)) {
        $null = Invoke-AdbRaw @("shell", "am", "force-stop", $packageName)
    }
    Restore-UnattendedDeviceSession
    $restoreFailureMessage = if ($null -eq $script:DeviceInteractive) {
        ""
    }
    else {
        [string]$script:DeviceInteractive['restoreError']
    }
    $writtenSummaryPath = Join-Path $artifactDirectory "summary.json"
    if ($null -ne $script:DeviceInteractive -and
        (Test-Path -LiteralPath $writtenSummaryPath -PathType Leaf)) {
        try {
            $writtenSummary = Get-Content -LiteralPath $writtenSummaryPath -Raw | ConvertFrom-Json -Depth 64
            $writtenSummary.deviceInteractive.restored = [bool]$script:DeviceInteractive['restored']
            $writtenSummary.deviceInteractive.restoreError = [string]$script:DeviceInteractive['restoreError']
            if (-not [string]::IsNullOrWhiteSpace($restoreFailureMessage)) {
                $writtenSummary.passed = $false
                $restoreErrorText = "Device-interactive state restoration failed: $restoreFailureMessage"
                if ($null -ne $writtenSummary.PSObject.Properties['error']) {
                    $writtenSummary.error = (([string]$writtenSummary.error) + "`n" + $restoreErrorText).Trim()
                }
                else {
                    $writtenSummary | Add-Member -NotePropertyName error -NotePropertyValue $restoreErrorText
                }
            }
            Write-Utf8Json $writtenSummaryPath $writtenSummary
        }
        catch {
            Write-Host "Could not append device-interactive restore state to summary: $($_.Exception.Message)"
        }
    }
    if (-not $succeeded) {
        Write-Host "Android device test failed. Artifacts: $artifactDirectory"
    }
    if ($succeeded -and -not [string]::IsNullOrWhiteSpace($restoreFailureMessage)) {
        throw "Device-interactive state restoration failed: $restoreFailureMessage"
    }
}
