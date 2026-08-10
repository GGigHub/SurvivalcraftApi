[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$ModPackagePath,
    [string]$HostAssemblyDirectory,
    [string]$CecilAssemblyPath,
    [string]$OutputPath,
    [string[]]$AssemblyScope = @(
        "Survivalcraft",
        "Engine",
        "EntitySystem"
    ),
    [ValidateRange(1, 8192)]
    [int]$MaximumEntries = 4096,
    [ValidateRange(1, 1073741824)]
    [long]$MaximumExpandedBytes = 536870912
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$startedUtc = [DateTimeOffset]::UtcNow

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
    $heading1 = [System.Text.Encoding]::UTF8.GetBytes(
        "有头有脸天才少年,耍猴表演敢为人先")
    $heading2 = [System.Text.Encoding]::UTF8.GetBytes(
        "修改他人mod请获得原作者授权，否则小心出名！")
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
                $decoded[$index] = $Bytes[
                    $heading2.Length + $secondOffset + $secondIndex]
                $secondIndex++
            }
        }
        return ,$decoded
    }
    return ,$Bytes
}

function Write-Utf8Json {
    param([string]$Path, [object]$Value)
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [System.IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 40) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Resolve-ExistingFile {
    param([string]$Path, [string]$Description)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "$Description '$fullPath' does not exist."
    }
    return (Resolve-Path -LiteralPath $fullPath).Path
}

function Get-AllCecilTypes {
    param([Mono.Cecil.ModuleDefinition]$Module)
    $pending = [System.Collections.Generic.Queue[Mono.Cecil.TypeDefinition]]::new()
    foreach ($type in $Module.Types) {
        $pending.Enqueue($type)
    }
    while ($pending.Count -gt 0) {
        $type = $pending.Dequeue()
        Write-Output $type
        foreach ($nestedType in $type.NestedTypes) {
            $pending.Enqueue($nestedType)
        }
    }
}

if ([string]::IsNullOrWhiteSpace($HostAssemblyDirectory)) {
    $hostCandidates = @(
        (Join-Path $repositoryRoot "Survivalcraft.Android\obj\Release\net10.0-android\android-arm64\android\assets\arm64-v8a"),
        (Join-Path $repositoryRoot "Survivalcraft.Windows\bin\Release\net10.0-windows\win-x64"),
        (Join-Path $repositoryRoot "Survivalcraft.Test\bin\Debug\net10.0-windows\win-x64")
    )
    $HostAssemblyDirectory = $hostCandidates |
        Where-Object {
            Test-Path -LiteralPath (Join-Path $_ "Survivalcraft.dll") -PathType Leaf
        } |
        Sort-Object {
            (Get-Item -LiteralPath (Join-Path $_ "Survivalcraft.dll")).LastWriteTimeUtc
        } -Descending |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($HostAssemblyDirectory)) {
    throw "No built Survivalcraft host assembly directory was found. Build the test APK or Windows test host first."
}
$HostAssemblyDirectory = (Resolve-Path -LiteralPath $HostAssemblyDirectory).Path
foreach ($scope in $AssemblyScope) {
    if (-not (Test-Path -LiteralPath (Join-Path $HostAssemblyDirectory "$scope.dll") -PathType Leaf)) {
        throw "Host scope '$scope' is missing from '$HostAssemblyDirectory'."
    }
}

if ([string]::IsNullOrWhiteSpace($CecilAssemblyPath)) {
    $CecilAssemblyPath = Join-Path $HostAssemblyDirectory "Mono.Cecil.dll"
}
$CecilAssemblyPath = Resolve-ExistingFile $CecilAssemblyPath "Mono.Cecil assembly"
[System.Reflection.Assembly]::LoadFrom($CecilAssemblyPath) | Out-Null
Add-Type -AssemblyName System.IO.Compression

$resolvedPackages = @($ModPackagePath | ForEach-Object {
    Resolve-ExistingFile $_ "Mod package"
})
if ($resolvedPackages.Count -eq 0) {
    throw "At least one mod package is required."
}
if (@($resolvedPackages | Sort-Object -Unique).Count -ne $resolvedPackages.Count) {
    throw "ModPackagePath contains duplicate packages."
}

$scopeSet = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($scope in $AssemblyScope) {
    if (-not $scopeSet.Add($scope)) {
        throw "AssemblyScope contains duplicate scope '$scope'."
    }
}

$assemblyResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$assemblyResolver.AddSearchDirectory($HostAssemblyDirectory)
$readerParameters = [Mono.Cecil.ReaderParameters]::new()
$readerParameters.AssemblyResolver = $assemblyResolver
$packageResults = [System.Collections.Generic.List[object]]::new()
$totalAssemblyCount = 0
$totalMemberReferences = 0
$totalTypeReferences = 0
$totalUnresolvedMembers = 0
$totalUnresolvedTypes = 0
$totalNameOnlyHarmonyPatches = 0
$totalAmbiguousNameOnlyHarmonyPatches = 0
$failureMessage = ""

try {
    foreach ($packagePath in $resolvedPackages) {
        $originalBytes = [System.IO.File]::ReadAllBytes($packagePath)
        $decodedBytes = Get-DecipheredModBytes $originalBytes
        $packageHash = [System.Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($originalBytes)
        ).ToLowerInvariant()
        $decodedStream = [System.IO.MemoryStream]::new($decodedBytes, $false)
        $archive = $null
        try {
            $archive = [System.IO.Compression.ZipArchive]::new(
                $decodedStream,
                [System.IO.Compression.ZipArchiveMode]::Read,
                $false)
            $entries = @($archive.Entries)
            if ($entries.Count -eq 0 -or $entries.Count -gt $MaximumEntries) {
                throw "Package '$packagePath' contains $($entries.Count) entries; allowed range is 1 through $MaximumEntries."
            }
            [long]$expandedBytes = 0
            foreach ($entry in $entries) {
                $expandedBytes += $entry.Length
                if ($expandedBytes -gt $MaximumExpandedBytes) {
                    throw "Package '$packagePath' expands past $MaximumExpandedBytes bytes."
                }
            }

            $assemblyResults = [System.Collections.Generic.List[object]]::new()
            foreach ($entry in @($entries | Where-Object {
                [System.IO.Path]::GetExtension($_.FullName) -ieq ".dll"
            })) {
                $entryStream = $entry.Open()
                $assemblyStream = [System.IO.MemoryStream]::new()
                try {
                    $entryStream.CopyTo($assemblyStream)
                    $assemblyStream.Position = 0
                    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly(
                        $assemblyStream,
                        $readerParameters)
                    try {
                        $memberReferences = @($assembly.MainModule.GetMemberReferences() |
                            Where-Object {
                                $scopeSet.Contains($_.DeclaringType.Scope.Name)
                            })
                        $typeReferences = @($assembly.MainModule.GetTypeReferences() |
                            Where-Object { $scopeSet.Contains($_.Scope.Name) })
                        $unresolvedMembers = [System.Collections.Generic.List[object]]::new()
                        foreach ($reference in $memberReferences) {
                            try {
                                if ($null -eq $reference.Resolve()) {
                                    $unresolvedMembers.Add([ordered]@{
                                        scope = $reference.DeclaringType.Scope.Name
                                        signature = $reference.FullName
                                        error = "Resolve returned null."
                                    })
                                }
                            }
                            catch {
                                $unresolvedMembers.Add([ordered]@{
                                    scope = $reference.DeclaringType.Scope.Name
                                    signature = $reference.FullName
                                    error = $_.Exception.Message
                                })
                            }
                        }
                        $unresolvedTypes = [System.Collections.Generic.List[object]]::new()
                        foreach ($reference in $typeReferences) {
                            try {
                                if ($null -eq $reference.Resolve()) {
                                    $unresolvedTypes.Add([ordered]@{
                                        scope = $reference.Scope.Name
                                        signature = $reference.FullName
                                        error = "Resolve returned null."
                                    })
                                }
                            }
                            catch {
                                $unresolvedTypes.Add([ordered]@{
                                    scope = $reference.Scope.Name
                                    signature = $reference.FullName
                                    error = $_.Exception.Message
                                })
                            }
                        }

                        $nameOnlyHarmonyPatches =
                            [System.Collections.Generic.List[object]]::new()
                        $ambiguousNameOnlyHarmonyPatches =
                            [System.Collections.Generic.List[object]]::new()
                        foreach ($type in Get-AllCecilTypes $assembly.MainModule) {
                            $attributeOwners = @($type) + @($type.Methods)
                            foreach ($owner in $attributeOwners) {
                                foreach ($attribute in @($owner.CustomAttributes |
                                    Where-Object {
                                        $_.AttributeType.FullName -eq
                                            "HarmonyLib.HarmonyPatch"
                                    })) {
                                    if (($attribute.ConstructorArguments.Count -ne 2) -or
                                        ($attribute.ConstructorArguments[0].Type.FullName -ne
                                            "System.Type") -or
                                        ($attribute.ConstructorArguments[1].Type.FullName -ne
                                            "System.String")) {
                                        continue
                                    }
                                    $targetType = $attribute.ConstructorArguments[0].Value
                                    $targetMethod =
                                        [string]$attribute.ConstructorArguments[1].Value
                                    if (($null -eq $targetType) -or
                                        [string]::IsNullOrWhiteSpace($targetMethod) -or
                                        (-not $scopeSet.Contains($targetType.Scope.Name))) {
                                        continue
                                    }
                                    $targetDefinition = $targetType.Resolve()
                                    $candidates = @($targetDefinition.Methods |
                                        Where-Object { $_.Name -eq $targetMethod })
                                    $record = [ordered]@{
                                        owner = $owner.FullName
                                        targetType = $targetType.FullName
                                        targetMethod = $targetMethod
                                        candidateCount = $candidates.Count
                                        candidates = @($candidates |
                                            ForEach-Object { $_.FullName })
                                    }
                                    $nameOnlyHarmonyPatches.Add($record)
                                    if ($candidates.Count -gt 1) {
                                        $ambiguousNameOnlyHarmonyPatches.Add($record)
                                    }
                                }
                            }
                        }

                        $totalAssemblyCount++
                        $totalMemberReferences += $memberReferences.Count
                        $totalTypeReferences += $typeReferences.Count
                        $totalUnresolvedMembers += $unresolvedMembers.Count
                        $totalUnresolvedTypes += $unresolvedTypes.Count
                        $totalNameOnlyHarmonyPatches += $nameOnlyHarmonyPatches.Count
                        $totalAmbiguousNameOnlyHarmonyPatches +=
                            $ambiguousNameOnlyHarmonyPatches.Count
                        $assemblyResults.Add([ordered]@{
                            entry = $entry.FullName
                            assembly = $assembly.Name.FullName
                            memberReferenceCount = $memberReferences.Count
                            typeReferenceCount = $typeReferences.Count
                            unresolvedMemberCount = $unresolvedMembers.Count
                            unresolvedTypeCount = $unresolvedTypes.Count
                            unresolvedMembers = @($unresolvedMembers)
                            unresolvedTypes = @($unresolvedTypes)
                            nameOnlyHarmonyPatchCount = $nameOnlyHarmonyPatches.Count
                            ambiguousNameOnlyHarmonyPatchCount =
                                $ambiguousNameOnlyHarmonyPatches.Count
                            nameOnlyHarmonyPatches = @($nameOnlyHarmonyPatches)
                            ambiguousNameOnlyHarmonyPatches =
                                @($ambiguousNameOnlyHarmonyPatches)
                        })
                    }
                    finally {
                        $assembly.Dispose()
                    }
                }
                finally {
                    $assemblyStream.Dispose()
                    $entryStream.Dispose()
                }
            }
            if ($assemblyResults.Count -eq 0) {
                throw "Package '$packagePath' contains no DLL assembly."
            }
            $packageResults.Add([ordered]@{
                path = $packagePath
                sha256 = $packageHash
                entryCount = $entries.Count
                expandedBytes = $expandedBytes
                assemblies = @($assemblyResults)
            })
        }
        finally {
            if ($null -ne $archive) {
                $archive.Dispose()
            }
            $decodedStream.Dispose()
        }
    }
}
catch {
    $failureMessage = $_.Exception.ToString()
}
finally {
    $assemblyResolver.Dispose()
}

$passed = [string]::IsNullOrWhiteSpace($failureMessage) `
    -and $totalUnresolvedMembers -eq 0 `
    -and $totalUnresolvedTypes -eq 0 `
    -and $totalAmbiguousNameOnlyHarmonyPatches -eq 0
if (-not $passed -and [string]::IsNullOrWhiteSpace($failureMessage)) {
    $failureMessage = "Found $totalUnresolvedMembers unresolved member reference(s), " +
        "$totalUnresolvedTypes unresolved type reference(s), and " +
        "$totalAmbiguousNameOnlyHarmonyPatches ambiguous name-only Harmony patch(es)."
}
$hostAssemblies = @($AssemblyScope | ForEach-Object {
    $path = Join-Path $HostAssemblyDirectory "$_.dll"
    [ordered]@{
        name = $_
        path = $path
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
    }
})
$summary = [ordered]@{
    schemaVersion = 1
    passed = $passed
    error = $failureMessage
    startedUtc = $startedUtc.ToString("O")
    completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    readOnly = $true
    hostAssemblyDirectory = $HostAssemblyDirectory
    hostAssemblies = $hostAssemblies
    cecilAssemblyPath = $CecilAssemblyPath
    assemblyScopes = @($AssemblyScope)
    packageCount = $resolvedPackages.Count
    assemblyCount = $totalAssemblyCount
    memberReferenceCount = $totalMemberReferences
    typeReferenceCount = $totalTypeReferences
    unresolvedMemberCount = $totalUnresolvedMembers
    unresolvedTypeCount = $totalUnresolvedTypes
    nameOnlyHarmonyPatchCount = $totalNameOnlyHarmonyPatches
    ambiguousNameOnlyHarmonyPatchCount =
        $totalAmbiguousNameOnlyHarmonyPatches
    packages = @($packageResults)
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    Write-Utf8Json $OutputPath $summary
}

if (-not $passed) {
    Write-Host "MOD BINARY COMPATIBILITY FAILED"
    Write-Host "unresolvedMembers=$totalUnresolvedMembers"
    Write-Host "unresolvedTypes=$totalUnresolvedTypes"
    Write-Host "ambiguousNameOnlyHarmonyPatches=$totalAmbiguousNameOnlyHarmonyPatches"
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        Write-Host "summary=$OutputPath"
    }
    throw $failureMessage
}

Write-Host "MOD BINARY COMPATIBILITY PASSED"
Write-Host "packages=$($resolvedPackages.Count)"
Write-Host "assemblies=$totalAssemblyCount"
Write-Host "memberReferences=$totalMemberReferences"
Write-Host "typeReferences=$totalTypeReferences"
Write-Host "nameOnlyHarmonyPatches=$totalNameOnlyHarmonyPatches"
Write-Host "ambiguousNameOnlyHarmonyPatches=$totalAmbiguousNameOnlyHarmonyPatches"
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    Write-Host "summary=$OutputPath"
}
