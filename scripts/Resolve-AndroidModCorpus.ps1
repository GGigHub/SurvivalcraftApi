[CmdletBinding()]
param(
    [string]$ManifestPath,
    [string]$LockPath,
    [string]$OutputDirectory,
    [string[]]$ModId = @(),
    [switch]$UpdateLock,
    [switch]$Offline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$defaultManifestPath = Join-Path $repositoryRoot "tests\android-mod-corpus\manifest.json"
$ManifestPath = if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $defaultManifestPath
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
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $artifactsRoot "android-mod-corpus"
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$packagesRoot = Join-Path $outputRoot "packages"
$summaryPath = Join-Path $outputRoot "resolution.json"
$startedUtc = [DateTimeOffset]::UtcNow

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
        ($Value | ConvertTo-Json -Depth 40) + [Environment]::NewLine,
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

function Test-BytePrefix {
    param([byte[]]$Bytes, [byte[]]$Prefix)
    if ($Bytes.Length -lt $Prefix.Length) {
        return $false
    }
    for ($index = 0; $index -lt $Prefix.Length; $index++) {
        if ($Bytes[$index] -ne $Prefix[$index]) {
            return $false
        }
    }
    return $true
}

function Get-DecipheredModBytes {
    param([byte[]]$Bytes)
    $heading1 = [System.Text.Encoding]::UTF8.GetBytes("有头有脸天才少年,耍猴表演敢为人先")
    $heading2 = [System.Text.Encoding]::UTF8.GetBytes("修改他人mod请获得原作者授权，否则小心出名！")
    if (Test-BytePrefix $Bytes $heading1) {
        $decoded = [byte[]]::new($Bytes.Length - $heading1.Length)
        for ($index = 0; $index -lt $decoded.Length; $index++) {
            $decoded[$index] = $Bytes[$Bytes.Length - 1 - $index]
        }
        return ,$decoded
    }
    if (Test-BytePrefix $Bytes $heading2) {
        $decoded = [byte[]]::new($Bytes.Length - $heading2.Length)
        $firstIndex = 0
        $secondIndex = 0
        $secondOffset = [int](($decoded.Length + 1) / 2)
        for ($index = 0; $index -lt $decoded.Length; $index++) {
            if (($index % 2) -eq 0) {
                $decoded[$index] = $Bytes[$heading2.Length + $firstIndex]
                $firstIndex++
            }
            else {
                $decoded[$index] = $Bytes[$heading2.Length + $secondOffset + $secondIndex]
                $secondIndex++
            }
        }
        return ,$decoded
    }
    return ,$Bytes
}

function Read-ModArchiveMetadata {
    param(
        [string]$Path,
        [long]$MaximumExpandedBytes,
        [int]$MaximumEntries,
        [double]$MaximumCompressionRatio
    )
    $originalBytes = [System.IO.File]::ReadAllBytes($Path)
    $decodedBytes = Get-DecipheredModBytes $originalBytes
    $decodedHash = [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($decodedBytes)).ToLowerInvariant()
    $memoryStream = [System.IO.MemoryStream]::new($decodedBytes, $false)
    $archive = $null
    try {
        try {
            $archive = [System.IO.Compression.ZipArchive]::new(
                $memoryStream,
                [System.IO.Compression.ZipArchiveMode]::Read,
                $false)
        }
        catch {
            throw "'$Path' is not a readable .scmod ZIP archive after applying supported deciphering: $($_.Exception.Message)"
        }
        $entries = @($archive.Entries)
        if ($entries.Count -eq 0 -or $entries.Count -gt $MaximumEntries) {
            throw "'$Path' contains $($entries.Count) archive entries; allowed range is 1 through $MaximumEntries."
        }
        [long]$expandedBytes = 0
        $extensions = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        $normalizedEntryNames = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $entries) {
            $entryName = [string]$entry.FullName
            $normalizedEntryName = $entryName.Replace("\", "/")
            if ([string]::IsNullOrWhiteSpace($entryName) -or
                $normalizedEntryName -match "(^/|^[A-Za-z]:|(^|/)\.\.(/|$)|[\x00])") {
                throw "'$Path' contains unsafe archive entry '$entryName'."
            }
            if (-not $normalizedEntryNames.Add($normalizedEntryName)) {
                throw "'$Path' contains duplicate normalized archive entry '$normalizedEntryName'."
            }
            if ($entry.Length -lt 0 -or $entry.Length -gt $MaximumExpandedBytes) {
                throw "Archive entry '$entryName' has invalid expanded size $($entry.Length)."
            }
            $expandedBytes += $entry.Length
            if ($expandedBytes -gt $MaximumExpandedBytes) {
                throw "'$Path' expands to more than $MaximumExpandedBytes bytes."
            }
            if ($entry.Length -gt 0) {
                if ($entry.CompressedLength -le 0) {
                    throw "Archive entry '$entryName' has a nonzero expanded size but no compressed payload."
                }
                $ratio = [double]$entry.Length / [double]$entry.CompressedLength
                if ($ratio -gt $MaximumCompressionRatio) {
                    throw "Archive entry '$entryName' exceeds compression-ratio limit $MaximumCompressionRatio (actual $([Math]::Round($ratio, 2)))."
                }
            }
            $extension = [System.IO.Path]::GetExtension($normalizedEntryName)
            if (-not [string]::IsNullOrWhiteSpace($extension)) {
                $null = $extensions.Add($extension.ToLowerInvariant())
            }
        }
        $modInfoEntries = @($entries | Where-Object {
            ([string]$_.FullName).Replace("\", "/") -ceq "modinfo.json"
        })
        if ($modInfoEntries.Count -ne 1) {
            throw "'$Path' must contain exactly one case-sensitive root entry named 'modinfo.json'."
        }
        $modInfoEntry = $modInfoEntries[0]
        if ($modInfoEntry.Length -le 0 -or $modInfoEntry.Length -gt 1MB) {
            throw "modinfo.json in '$Path' has invalid size $($modInfoEntry.Length)."
        }
        $entryStream = $modInfoEntry.Open()
        $reader = [System.IO.StreamReader]::new(
            $entryStream,
            [System.Text.UTF8Encoding]::new($false, $true),
            $true)
        try {
            $modInfoText = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
            $entryStream.Dispose()
        }
        try {
            $modInfo = $modInfoText | ConvertFrom-Json -Depth 64
        }
        catch {
            throw "modinfo.json in '$Path' is not valid UTF-8 JSON: $($_.Exception.Message)"
        }
        $packageName = ([string](Get-PropertyValue $modInfo "PackageName" "")).Trim()
        $name = ([string](Get-PropertyValue $modInfo "Name" "")).Trim()
        $version = ([string](Get-PropertyValue $modInfo "Version" "")).Trim()
        $apiVersion = ([string](Get-PropertyValue $modInfo "ApiVersion" "")).Trim()
        $scVersion = ([string](Get-PropertyValue $modInfo "ScVersion" "")).Trim()
        if ([string]::IsNullOrWhiteSpace($packageName) -or
            $packageName.Length -gt 200 -or
            $packageName -match "[;\r\n]") {
            throw "modinfo.json in '$Path' has an invalid PackageName."
        }
        if ([string]::Equals($packageName, "survivalcraft", [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($packageName, "fastdebug", [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "modinfo.json in '$Path' attempts to use reserved PackageName '$packageName'."
        }
        foreach ($required in @(
            @{ Label = "Name"; Value = $name },
            @{ Label = "Version"; Value = $version },
            @{ Label = "ApiVersion"; Value = $apiVersion })) {
            if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
                throw "modinfo.json in '$Path' is missing required field '$($required.Label)'."
            }
        }
        $dependencies = [System.Collections.Generic.List[string]]::new()
        $dependencyValue = Get-PropertyValue $modInfo "Dependencies" @()
        if ($dependencyValue -is [System.Management.Automation.PSCustomObject]) {
            foreach ($property in $dependencyValue.PSObject.Properties) {
                $dependencies.Add([string]$property.Name)
            }
        }
        else {
            foreach ($dependency in @($dependencyValue)) {
                $dependencyText = ([string]$dependency).Trim()
                if (-not [string]::IsNullOrWhiteSpace($dependencyText)) {
                    $dependencyName = ($dependencyText -split ":", 2)[0].Trim()
                    if (-not [string]::IsNullOrWhiteSpace($dependencyName)) {
                        $dependencies.Add($dependencyName)
                    }
                }
            }
        }
        return [ordered]@{
            packageName = $packageName
            name = $name
            version = $version
            apiVersion = $apiVersion
            scVersion = $scVersion
            dependencies = @($dependencies | Sort-Object -Unique)
            entryCount = $entries.Count
            expandedBytes = $expandedBytes
            decodedBytes = $decodedBytes.Length
            decodedSha256 = $decodedHash
            deciphered = $decodedBytes.Length -ne $originalBytes.Length
            extensions = @($extensions | Sort-Object)
        }
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        else {
            $memoryStream.Dispose()
        }
    }
}

function Assert-AllowedHttpsUrl {
    param([string]$Url, [string[]]$AllowedHosts, [string]$Label)
    [System.Uri]$uri = $null
    if (-not [System.Uri]::TryCreate($Url, [System.UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne "https") {
        throw "$Label must be an absolute HTTPS URL."
    }
    if (-not ($AllowedHosts | Where-Object {
        [string]::Equals($_, $uri.DnsSafeHost, [System.StringComparison]::OrdinalIgnoreCase)
    })) {
        throw "$Label host '$($uri.DnsSafeHost)' is not in allowedHosts."
    }
    return $uri
}

function Invoke-DirectDownload {
    param(
        [string]$Url,
        [string]$Destination,
        [long]$MaximumBytes,
        [string[]]$AllowedHosts
    )
    $sourceUri = Assert-AllowedHttpsUrl $Url $AllowedHosts "sourceUrl"
    $curlPath = (Get-Command curl.exe -ErrorAction Stop).Source
    $arguments = @(
        "--noproxy", "*",
        "--globoff",
        "--fail-with-body",
        "--silent",
        "--show-error",
        "--location",
        "--proto", "=https",
        "--proto-redir", "=https",
        "--connect-timeout", "20",
        "--max-time", "180",
        "--max-filesize", [string]$MaximumBytes,
        "--output", $Destination,
        "--write-out", "%{url_effective}",
        $sourceUri.AbsoluteUri)
    $output = @(& $curlPath @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Direct download failed for '$Url' with curl exit code $LASTEXITCODE`: $($output -join [Environment]::NewLine)"
    }
    $effectiveUrl = ($output -join "").Trim()
    $null = Assert-AllowedHttpsUrl $effectiveUrl $AllowedHosts "effective download URL"
    return $effectiveUrl
}

if (-not (Test-PathInside $outputRoot $artifactsRoot)) {
    throw "OutputDirectory must be below '$artifactsRoot'."
}
if (-not (Test-PathInside $LockPath $repositoryRoot)) {
    throw "LockPath must be below '$repositoryRoot'."
}
if ($UpdateLock -and $Offline) {
    throw "UpdateLock cannot be combined with Offline."
}
if ($UpdateLock -and @($ModId).Count -gt 0) {
    throw "UpdateLock must resolve the complete manifest; omit ModId."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null

try {
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 64
    if ([int](Get-PropertyValue $manifest "schemaVersion" 0) -ne 1) {
        throw "Unsupported Android mod corpus manifest schemaVersion."
    }
    $allowedHosts = @((Get-PropertyValue $manifest "allowedHosts" @()) | ForEach-Object {
        ([string]$_).Trim().ToLowerInvariant()
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    if ($allowedHosts.Count -eq 0) {
        throw "Manifest must define at least one allowedHosts entry."
    }
    $limits = Get-PropertyValue $manifest "limits" $null
    [long]$maximumDownloadBytes = Get-PropertyValue $limits "maximumDownloadBytes" 67108864
    [long]$maximumExpandedBytes = Get-PropertyValue $limits "maximumExpandedBytes" 268435456
    [int]$maximumEntries = Get-PropertyValue $limits "maximumEntries" 4096
    [double]$maximumCompressionRatio = Get-PropertyValue $limits "maximumCompressionRatio" 500
    if ($maximumDownloadBytes -le 0 -or $maximumDownloadBytes -gt 512MB -or
        $maximumExpandedBytes -le 0 -or $maximumExpandedBytes -gt 2GB -or
        $maximumEntries -le 0 -or $maximumEntries -gt 100000 -or
        $maximumCompressionRatio -lt 1 -or $maximumCompressionRatio -gt 10000) {
        throw "Manifest archive limits are outside the supported safety bounds."
    }

    $requestedIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($requestedIdValue in @($ModId)) {
        $requestedId = ([string]$requestedIdValue).Trim()
        if (-not $requestedIds.Add($requestedId)) {
            throw "ModId '$requestedId' was requested more than once."
        }
    }
    $manifestIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $selectedMods = [System.Collections.Generic.List[object]]::new()
    foreach ($mod in @((Get-PropertyValue $manifest "mods" @()))) {
        $id = ([string](Get-PropertyValue $mod "id" "")).Trim()
        if ($id -notmatch "^[a-z0-9][a-z0-9._-]{0,63}$") {
            throw "Manifest mod id '$id' is invalid."
        }
        if (-not $manifestIds.Add($id)) {
            throw "Manifest mod id '$id' is duplicated."
        }
        $enabled = [bool](Get-PropertyValue $mod "enabled" $true)
        if ($enabled -and ($requestedIds.Count -eq 0 -or $requestedIds.Contains($id))) {
            $selectedMods.Add($mod)
        }
    }
    foreach ($requestedId in $requestedIds) {
        if (-not $manifestIds.Contains($requestedId)) {
            throw "Requested ModId '$requestedId' is not in the manifest."
        }
    }
    if ($selectedMods.Count -eq 0) {
        throw "No enabled manifest mods were selected."
    }

    $lock = $null
    $lockById = @{}
    if (Test-Path -LiteralPath $LockPath -PathType Leaf) {
        $lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json -Depth 64
        if ([int](Get-PropertyValue $lock "schemaVersion" 0) -ne 1) {
            throw "Unsupported Android mod corpus lock schemaVersion."
        }
        foreach ($lockedMod in @((Get-PropertyValue $lock "mods" @()))) {
            $lockedId = ([string](Get-PropertyValue $lockedMod "id" "")).Trim()
            if ($lockById.ContainsKey($lockedId)) {
                throw "Lock contains duplicate mod id '$lockedId'."
            }
            $lockById[$lockedId] = $lockedMod
        }
    }
    elseif (-not $UpdateLock) {
        throw "Corpus lock '$LockPath' is missing. Run once with -UpdateLock and review the generated hashes."
    }

    $resolved = [System.Collections.Generic.List[object]]::new()
    foreach ($mod in $selectedMods) {
        $id = ([string](Get-PropertyValue $mod "id" "")).Trim()
        $sourceUrl = ([string](Get-PropertyValue $mod "sourceUrl" "")).Trim()
        $null = Assert-AllowedHttpsUrl $sourceUrl $allowedHosts "sourceUrl for '$id'"
        $expectedBytesValue = Get-PropertyValue $mod "expectedBytes" 0
        [long]$expectedBytes = if ($null -eq $expectedBytesValue) { 0 } else { $expectedBytesValue }
        if ($expectedBytes -lt 0 -or $expectedBytes -gt $maximumDownloadBytes) {
            throw "Manifest expectedBytes for '$id' is outside the download limit."
        }
        $lockedMod = if ($lockById.ContainsKey($id)) { $lockById[$id] } else { $null }
        $expectedHash = ""
        if (-not $UpdateLock) {
            if ($null -eq $lockedMod) {
                throw "Corpus lock has no entry for '$id'."
            }
            $lockedUrl = ([string](Get-PropertyValue $lockedMod "sourceUrl" "")).Trim()
            if (-not [string]::Equals($lockedUrl, $sourceUrl, [System.StringComparison]::Ordinal)) {
                throw "Corpus source URL for '$id' changed without refreshing the lock."
            }
            $expectedHash = ([string](Get-PropertyValue $lockedMod "sha256" "")).Trim().ToLowerInvariant()
            if ($expectedHash -notmatch "^[0-9a-f]{64}$") {
                throw "Corpus lock hash for '$id' is invalid."
            }
        }

        $targetPath = Join-Path $packagesRoot "$id.scmod"
        $needDownload = -not (Test-Path -LiteralPath $targetPath -PathType Leaf)
        if (-not $needDownload -and -not $UpdateLock) {
            $cachedHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($cachedHash -ne $expectedHash) {
                if ($Offline) {
                    throw "Cached package '$id' does not match its lock hash in Offline mode."
                }
                Remove-Item -LiteralPath $targetPath -Force
                $needDownload = $true
            }
        }
        if ($needDownload -and $Offline) {
            throw "Cached package '$id' is missing in Offline mode."
        }

        $effectiveUrl = $sourceUrl
        if ($needDownload -or $UpdateLock) {
            $temporaryPath = "$targetPath.partial-$PID"
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
            try {
                $effectiveUrl = Invoke-DirectDownload $sourceUrl $temporaryPath $maximumDownloadBytes $allowedHosts
                $temporaryFile = Get-Item -LiteralPath $temporaryPath
                if ($temporaryFile.Length -le 0 -or $temporaryFile.Length -gt $maximumDownloadBytes) {
                    throw "Downloaded package '$id' has invalid size $($temporaryFile.Length)."
                }
                if ($expectedBytes -gt 0 -and $temporaryFile.Length -ne $expectedBytes) {
                    throw "Downloaded package '$id' size $($temporaryFile.Length) does not match manifest expectedBytes $expectedBytes."
                }
                $temporaryHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
                if (-not $UpdateLock -and $temporaryHash -ne $expectedHash) {
                    throw "Downloaded package '$id' SHA-256 '$temporaryHash' does not match lock '$expectedHash'."
                }
                $null = Read-ModArchiveMetadata $temporaryPath $maximumExpandedBytes $maximumEntries $maximumCompressionRatio
                Move-Item -LiteralPath $temporaryPath -Destination $targetPath -Force
            }
            finally {
                if (Test-Path -LiteralPath $temporaryPath) {
                    Remove-Item -LiteralPath $temporaryPath -Force
                }
            }
        }

        $file = Get-Item -LiteralPath $targetPath
        if ($file.Length -le 0 -or $file.Length -gt $maximumDownloadBytes) {
            throw "Cached package '$id' has invalid size $($file.Length)."
        }
        if ($expectedBytes -gt 0 -and $file.Length -ne $expectedBytes) {
            throw "Cached package '$id' size $($file.Length) does not match manifest expectedBytes $expectedBytes."
        }
        $actualHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $UpdateLock -and $actualHash -ne $expectedHash) {
            throw "Cached package '$id' failed final lock verification."
        }
        $metadata = Read-ModArchiveMetadata $targetPath $maximumExpandedBytes $maximumEntries $maximumCompressionRatio
        $manifestPackageName = ([string](Get-PropertyValue $mod "expectedPackageName" "")).Trim()
        if (-not [string]::IsNullOrWhiteSpace($manifestPackageName) -and
            -not [string]::Equals($manifestPackageName, [string]$metadata.packageName, [System.StringComparison]::Ordinal)) {
            throw "PackageName for '$id' is '$($metadata.packageName)', expected '$manifestPackageName'."
        }
        $manifestVersion = ([string](Get-PropertyValue $mod "expectedVersion" "")).Trim()
        if (-not [string]::IsNullOrWhiteSpace($manifestVersion) -and
            -not [string]::Equals($manifestVersion, [string]$metadata.version, [System.StringComparison]::Ordinal)) {
            throw "Version for '$id' is '$($metadata.version)', expected '$manifestVersion'."
        }
        $resolved.Add([ordered]@{
            id = $id
            sourceUrl = $sourceUrl
            effectiveUrl = $effectiveUrl
            path = $targetPath
            sha256 = $actualHash
            bytes = $file.Length
            packageName = $metadata.packageName
            name = $metadata.name
            version = $metadata.version
            apiVersion = $metadata.apiVersion
            scVersion = $metadata.scVersion
            dependencies = $metadata.dependencies
            archive = [ordered]@{
                entryCount = $metadata.entryCount
                expandedBytes = $metadata.expandedBytes
                decodedBytes = $metadata.decodedBytes
                decodedSha256 = $metadata.decodedSha256
                deciphered = $metadata.deciphered
                extensions = $metadata.extensions
            }
        })
    }

    $duplicatePackages = @($resolved | Group-Object { [string]$_['packageName'] } | Where-Object { $_.Count -gt 1 })
    if ($duplicatePackages.Count -gt 0) {
        throw "Selected corpus contains duplicate PackageName values: $($duplicatePackages.Name -join ', ')."
    }

    if ($UpdateLock) {
        $lockDocument = [ordered]@{
            schemaVersion = 1
            generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
            manifest = [System.IO.Path]::GetRelativePath($repositoryRoot, $ManifestPath).Replace("\", "/")
            mods = @($resolved | ForEach-Object {
                [ordered]@{
                    id = $_.id
                    sourceUrl = $_.sourceUrl
                    effectiveUrl = $_.effectiveUrl
                    sha256 = $_.sha256
                    bytes = $_.bytes
                    packageName = $_.packageName
                    name = $_.name
                    version = $_.version
                    apiVersion = $_.apiVersion
                    scVersion = $_.scVersion
                    dependencies = $_.dependencies
                    archive = $_.archive
                }
            })
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $LockPath) -Force | Out-Null
        Write-Utf8Json $LockPath $lockDocument
    }

    $summary = [ordered]@{
        schemaVersion = 1
        passed = $true
        startedUtc = $startedUtc.ToString("O")
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        manifestPath = $ManifestPath
        lockPath = $LockPath
        lockUpdated = [bool]$UpdateLock
        offline = [bool]$Offline
        allowedHosts = $allowedHosts
        limits = [ordered]@{
            maximumDownloadBytes = $maximumDownloadBytes
            maximumExpandedBytes = $maximumExpandedBytes
            maximumEntries = $maximumEntries
            maximumCompressionRatio = $maximumCompressionRatio
        }
        mods = $resolved
        outputDirectory = $outputRoot
    }
    Write-Utf8Json $summaryPath $summary
    Write-Host "ANDROID MOD CORPUS RESOLUTION PASSED"
    Write-Host "mods=$($resolved.Count)"
    Write-Host "summary=$summaryPath"
}
catch {
    $failure = [ordered]@{
        schemaVersion = 1
        passed = $false
        startedUtc = $startedUtc.ToString("O")
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        manifestPath = $ManifestPath
        lockPath = $LockPath
        lockUpdated = [bool]$UpdateLock
        offline = [bool]$Offline
        error = $_.Exception.ToString()
        outputDirectory = $outputRoot
    }
    Write-Utf8Json $summaryPath $failure
    throw
}
