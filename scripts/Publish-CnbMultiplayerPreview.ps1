[CmdletBinding()]
param(
    [string]$ReleaseDirectory,
    [string]$Repository = "trk34Organization/SurvivalcraftApi"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseSpec = Get-Content (Join-Path $repositoryRoot "release\multiplayer-preview.json") -Raw |
    ConvertFrom-Json
$releasePath = if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    Join-Path $repositoryRoot "artifacts\releases\$($releaseSpec.tag)"
}
else {
    [System.IO.Path]::GetFullPath($ReleaseDirectory)
}
$apiRoot = "https://api.cnb.cool"
$repositoryApiPath = $Repository.Trim('/')

function Get-CnbToken {
    if (-not [string]::IsNullOrWhiteSpace($env:CNB_TOKEN)) {
        return $env:CNB_TOKEN
    }
    $credentialInput = "protocol=https`nhost=cnb.cool`n`n"
    $credentialLines = @($credentialInput | git credential fill 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "CNB_TOKEN is not set and Git Credential Manager could not provide a CNB credential."
    }
    foreach ($line in $credentialLines) {
        if ($line.StartsWith("password=", [System.StringComparison]::Ordinal)) {
            $token = $line.Substring("password=".Length)
            if (-not [string]::IsNullOrWhiteSpace($token)) {
                return $token
            }
        }
    }
    throw "CNB_TOKEN is not set and no CNB access token exists in Git Credential Manager."
}

function Invoke-CnbJson {
    param(
        [ValidateSet("Get", "Post", "Patch")]
        [string]$Method,
        [string]$Uri,
        $Body,
        [hashtable]$Headers
    )
    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $Headers
        ContentType = "application/json"
    }
    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 8 -Compress
    }
    return Invoke-RestMethod @parameters
}

if (-not (Test-Path -LiteralPath $releasePath -PathType Container)) {
    throw "Release directory does not exist: $releasePath"
}

Push-Location $repositoryRoot
try {
    $manifestPath = Join-Path $releasePath $releaseSpec.artifacts.releaseManifest
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ([bool]$manifest.sourceDirty) {
        throw "Refusing to publish artifacts built from a dirty source tree."
    }
    $headCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $headCommit -ne [string]$manifest.sourceCommit) {
        throw "Release manifest commit '$($manifest.sourceCommit)' does not match HEAD '$headCommit'."
    }
    $tagCommit = (& git rev-list -n 1 $releaseSpec.tag).Trim()
    if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $headCommit) {
        throw "Annotated tag '$($releaseSpec.tag)' does not resolve to release commit '$headCommit'."
    }
    $remoteTag = @(& git ls-remote --tags origin "refs/tags/$($releaseSpec.tag)*")
    if ($LASTEXITCODE -ne 0 -or $remoteTag.Count -eq 0) {
        throw "Tag '$($releaseSpec.tag)' is not available on origin."
    }

    $assetPaths = @(
        (Join-Path $releasePath $releaseSpec.artifacts.windowsX64),
        (Join-Path $releasePath $releaseSpec.artifacts.relayWindowsX64),
        (Join-Path $releasePath $releaseSpec.artifacts.androidArm64),
        $manifestPath,
        (Join-Path $releasePath $releaseSpec.artifacts.releaseNotes),
        (Join-Path $releasePath $releaseSpec.artifacts.checksums)
    )
    foreach ($assetPath in $assetPaths) {
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            throw "Release asset is missing: $assetPath"
        }
    }

    $token = Get-CnbToken
    $headers = @{
        Authorization = "Bearer $token"
        Accept = "application/vnd.cnb.api+json"
    }
    $releaseByTagUri = "$apiRoot/$repositoryApiPath/-/releases/tags/$([uri]::EscapeDataString($releaseSpec.tag))"
    $release = $null
    try {
        $release = Invoke-CnbJson -Method Get -Uri $releaseByTagUri -Body $null -Headers $headers
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) {
            throw
        }
    }

    $releaseBody = Get-Content (Join-Path $repositoryRoot "release\$($releaseSpec.tag).md") -Raw
    $form = [ordered]@{
        name = [string]$releaseSpec.title
        body = $releaseBody
        draft = $false
        prerelease = $true
        make_latest = "false"
    }
    if ($null -eq $release) {
        $createForm = [ordered]@{
            tag_name = [string]$releaseSpec.tag
            target_commitish = $headCommit
            name = $form.name
            body = $form.body
            draft = $form.draft
            prerelease = $form.prerelease
            make_latest = $form.make_latest
        }
        $release = Invoke-CnbJson `
            -Method Post `
            -Uri "$apiRoot/$repositoryApiPath/-/releases" `
            -Body $createForm `
            -Headers $headers
        Write-Host "created release tag=$($release.tag_name) id=$($release.id)"
    }
    else {
        Invoke-CnbJson `
            -Method Patch `
            -Uri "$apiRoot/$repositoryApiPath/-/releases/$($release.id)" `
            -Body $form `
            -Headers $headers | Out-Null
        Write-Host "updated release tag=$($release.tag_name) id=$($release.id)"
    }

    foreach ($assetPath in $assetPaths) {
        $asset = Get-Item -LiteralPath $assetPath
        $uploadForm = [ordered]@{
            asset_name = $asset.Name
            overwrite = $true
            size = $asset.Length
        }
        $upload = Invoke-CnbJson `
            -Method Post `
            -Uri "$apiRoot/$repositoryApiPath/-/releases/$($release.id)/asset-upload-url" `
            -Body $uploadForm `
            -Headers $headers
        Invoke-WebRequest `
            -Method Put `
            -Uri $upload.upload_url `
            -InFile $asset.FullName `
            -ContentType "application/octet-stream" | Out-Null
        Invoke-RestMethod `
            -Method Post `
            -Uri $upload.verify_url `
            -Headers $headers `
            -ContentType "application/json" | Out-Null
        Write-Host "uploaded asset=$($asset.Name) size=$($asset.Length)"
    }

    $published = Invoke-CnbJson -Method Get -Uri $releaseByTagUri -Body $null -Headers $headers
    foreach ($assetPath in $assetPaths) {
        $localAsset = Get-Item -LiteralPath $assetPath
        $remoteAsset = @($published.assets | Where-Object { $_.name -eq $localAsset.Name })
        if ($remoteAsset.Count -ne 1) {
            throw "Remote release does not contain exactly one '$($localAsset.Name)' asset."
        }
        if ([long]$remoteAsset[0].size -ne $localAsset.Length) {
            throw "Remote size mismatch for '$($localAsset.Name)'."
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$remoteAsset[0].hash_value)) {
            $localHash = (Get-FileHash -LiteralPath $localAsset.FullName -Algorithm SHA256).Hash
            $remoteHash = ([string]$remoteAsset[0].hash_value) -replace '^sha256:', ''
            if ($remoteHash -notmatch '^[0-9a-fA-F]{64}$' -or
                $remoteHash -ne $localHash) {
                throw "Remote SHA-256 mismatch for '$($localAsset.Name)'."
            }
        }
    }
    Write-Host "CNB RELEASE PUBLISH PASSED"
    Write-Host "url=https://cnb.cool/$Repository/-/releases/tag/$($releaseSpec.tag)"
}
finally {
    $token = $null
    Pop-Location
}
