using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace Game {
    internal static class LegacyModAssemblyCompatibility {
        internal const string CraftingRecipesReadOnlyGetterBreakId = "crafting-recipes-readonly-getter";
        internal const string LegacyParticleSystemOneArgumentBreakId =
            "legacy-particle-system-one-argument";
        internal const string FirearmsDisableSiyahAkrepBootstrapBreakId =
            "firearms-2.8-disable-siyah-akrep-bootstrap";
        internal const string FirearmsHostGunsDataBreakId =
            "firearms-2.8-host-guns-data";
        internal const string FirearmsHostRecipesBreakId =
            "firearms-2.8-host-recipes";
        internal const string FirearmsHostProjectileUpdateBreakId =
            "firearms-2.8-host-projectile-update";
        internal const string FirearmsHostMapInitializationBreakId =
            "firearms-2.8-host-map-initialization";
        internal const string FirearmsDisableSiyahAkrepStructuresBreakId =
            "firearms-2.8-disable-siyah-akrep-structures";
        internal const string FirearmsResetLightsaberColorsBreakId =
            "firearms-2.8-reset-lightsaber-colors";
        internal const string FirearmsRemapDynamicBlockIndicesBreakId =
            "firearms-2.8-remap-dynamic-block-indices";
        internal const string FirearmsResetAnimationStepsBreakId =
            "firearms-2.8-reset-animation-steps";
        internal const string FirearmsRemapGameWidgetTypeBreakId =
            "firearms-2.8-remap-gamewidget-type";
        internal const string FirearmsRemapTemperatureAdjustmentBreakId =
            "firearms-2.8-remap-temperature-adjustment";
        internal const string FirearmsRemapLegacyFieldShapesBreakId =
            "firearms-2.8-remap-legacy-field-shapes";
        internal const string FirearmsRemapPlayerCallbacksBreakId =
            "firearms-2.8-remap-player-callbacks";
        internal const string Firearms28AssemblySha256 =
            "fcd878f569ac4c825402c15d0ffc0af2a5df6e12ec7809b01969b360aa013559";
        internal const string Firearms28PackageSha256 =
            "1b4529c8db2af29e0eb14fc570ab3ab7ff8440c10261cbdcb2d14b29bd703b28";

        const string OldRecipesGetterName = "get_Recipes";
        const string CompatibilityRecipesMethodName = "GetRecipes1";
        const string CraftingRecipesManagerType = "Game.CraftingRecipesManager";
        const string OldRecipesReturnType = "Engine.ReadOnlyList`1<Game.CraftingRecipe>";
        const string SubsystemParticlesType = "SubsystemParticles";
        const string ParticleSystemBaseType = "Game.ParticleSystemBase";
        const string LegacyAddParticleSystemMethod = "AddParticleSystem";
        const string CompatibilityAddParticleSystemMethod = "AddLegacyParticleSystem";
        const string LegacyRemoveParticleSystemMethod = "RemoveParticleSystem";
        const string CompatibilityRemoveParticleSystemMethod = "RemoveLegacyParticleSystem";
        const string VoidTypeName = "Void";
        const string Firearms28PackageName = "gunsmod.ysf.scmod";
        const string Firearms28Version = "2.8";
        const string Firearms28ApiVersion = "1.45";
        const string Firearms28AssemblyName = "#Guns.dll";
        const string GunsModLoaderType = "Game.GunsModLoader";
        const string GunsBlocksInitializedMethod = "BlocksInitalized";
        const string SiyahAkrepFunctionsType = "Game.SAFunctions";
        const string SiyahAkrepInitializeMethod = "InitializeFunctions";
        const int SiyahAkrepBootstrapCallIlOffset = 0x0F;
        const string FirearmsFunctionsType = "Game.Functions";
        const string FirearmsInitializeGunsDataMethod = "InitializeGunsData";
        const string FirearmsWorldChestType = "Game.SAWorldChest";
        const string FirearmsBlockInitializeMethod = "Initialize";
        const int FirearmsInitializeGunsDataCallIlOffset = 0x00;
        const string FirearmsRecipesManagerType = "Game.FirearmsRecipesManager";
        const string FirearmsInitializeRecipesMethod = "Initialize";
        const int FirearmsInitializeRecipesCallIlOffset = 0x0A;
        const string FirearmsNewProjectilesType = "Game.SubsystemNewProjectiles";
        const string FirearmsProjectileUpdateMethod = "Update";
        const int FirearmsProjectileUpdateCodeSize = 0x16C9;
        const string FirearmsMapManagerType = "Game.MapManager";
        const string FirearmsMapInitializeMethod = "Initialize";
        const int FirearmsMapInitializeCallIlOffset = 0x14;
        const string FirearmsTerrainGeneratedMethod = "OnTerrainContentsGenerated";
        const int FirearmsStructuresCallIlOffset = 0xB4;
        const string FirearmsGenerateManagerType = "Game.SAGenerateManager";
        const string FirearmsHandleStructuresMethod = "HandleStructuresAddition";
        const string SurvivalcraftAssemblyName = "Survivalcraft";
        const string LegacyGameWidgetNamespace = "Game";
        const string GameWidgetTypeName = "GameWidget";
        const string SubsystemWeatherTypeName = "SubsystemWeather";
        const string LegacyTemperatureAdjustmentMethodName =
            "GetTemperatureAdjustmentAtHeight";
        const string CompatibilityTemperatureAdjustmentMethodName =
            "GetLegacyTemperatureAdjustmentAtHeight";

        internal static byte[] Apply(
            ModInfo modInfo,
            string assemblyFileName,
            byte[] assemblyBytes,
            out string[] appliedBreakIds,
            string sourcePackageSha256 = "") {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            appliedBreakIds = [];
            if (!IsPre181ApiVersion(modInfo?.ApiVersion)) {
                return assemblyBytes;
            }

            List<string> applied = [];
            byte[] rewritten = RewriteOldRecipesGetter(
                assemblyFileName,
                assemblyBytes,
                out bool recipesGetterRewritten);
            if (recipesGetterRewritten) {
                applied.Add(CraftingRecipesReadOnlyGetterBreakId);
            }
            rewritten = RewriteLegacyParticleSystemCalls(
                assemblyFileName,
                rewritten,
                out int particleSystemReferenceCount);
            if (particleSystemReferenceCount > 0) {
                applied.Add(LegacyParticleSystemOneArgumentBreakId);
            }

            if (IsExactFirearms28Assembly(
                    modInfo,
                    assemblyFileName,
                    assemblyBytes,
                    sourcePackageSha256)) {
                rewritten = RewriteStaticVoidCallToNops(
                    rewritten,
                    assemblyFileName,
                    GunsModLoaderType,
                    GunsBlocksInitializedMethod,
                    SiyahAkrepBootstrapCallIlOffset,
                    SiyahAkrepFunctionsType,
                    SiyahAkrepInitializeMethod);
                applied.Add(FirearmsDisableSiyahAkrepBootstrapBreakId);
                rewritten = RewriteStaticVoidCallToNops(
                    rewritten,
                    assemblyFileName,
                    FirearmsWorldChestType,
                    FirearmsBlockInitializeMethod,
                    FirearmsInitializeGunsDataCallIlOffset,
                    FirearmsFunctionsType,
                    FirearmsInitializeGunsDataMethod);
                applied.Add(FirearmsHostGunsDataBreakId);
                rewritten = RewriteStaticVoidCallToNops(
                    rewritten,
                    assemblyFileName,
                    GunsModLoaderType,
                    GunsBlocksInitializedMethod,
                    FirearmsInitializeRecipesCallIlOffset,
                    FirearmsRecipesManagerType,
                    FirearmsInitializeRecipesMethod);
                applied.Add(FirearmsHostRecipesBreakId);
                rewritten = RewriteFieldReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    "Projectile",
                    "IsInWater",
                    "LegacyIsInWater",
                    "System.Boolean");
                rewritten = RewriteInstanceVoidMethodToReturn(
                    rewritten,
                    assemblyFileName,
                    FirearmsNewProjectilesType,
                    FirearmsProjectileUpdateMethod,
                    [PrimitiveTypeCode.Single.ToString()],
                    FirearmsProjectileUpdateCodeSize);
                applied.Add(FirearmsHostProjectileUpdateBreakId);
                rewritten = RewriteStaticVoidCallToNops(
                    rewritten,
                    assemblyFileName,
                    GunsModLoaderType,
                    GunsBlocksInitializedMethod,
                    FirearmsMapInitializeCallIlOffset,
                    FirearmsMapManagerType,
                    FirearmsMapInitializeMethod);
                applied.Add(FirearmsHostMapInitializationBreakId);
                rewritten = RewriteStaticVoidCallToPops(
                    rewritten,
                    assemblyFileName,
                    GunsModLoaderType,
                    FirearmsTerrainGeneratedMethod,
                    ["Game.TerrainChunk"],
                    FirearmsStructuresCallIlOffset,
                    FirearmsGenerateManagerType,
                    FirearmsHandleStructuresMethod,
                    ["Game.SubsystemGMItemsBlockBehavior", "Game.TerrainChunk"]);
                applied.Add(FirearmsDisableSiyahAkrepStructuresBreakId);
                applied.Add(FirearmsResetLightsaberColorsBreakId);
                applied.Add(FirearmsRemapDynamicBlockIndicesBreakId);
                applied.Add(FirearmsResetAnimationStepsBreakId);
                rewritten = RewriteTypeReferenceNamespace(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    LegacyGameWidgetNamespace,
                    GameWidgetTypeName,
                    string.Empty);
                applied.Add(FirearmsRemapGameWidgetTypeBreakId);
                rewritten = RewriteMethodReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    SubsystemWeatherTypeName,
                    LegacyTemperatureAdjustmentMethodName,
                    CompatibilityTemperatureAdjustmentMethodName,
                    "System.Int32",
                    ["System.Int32"]);
                applied.Add(FirearmsRemapTemperatureAdjustmentBreakId);
                rewritten = RewriteFieldReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    "ClothingBlock",
                    "m_clothingData",
                    "m_legacyClothingData",
                    "Engine.DynamicArray`1<Game.ClothingData>");
                rewritten = RewriteFieldReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    "Projectile",
                    "Owner",
                    "LegacyOwner",
                    "Game.ComponentCreature");
                applied.Add(FirearmsRemapLegacyFieldShapesBreakId);
                rewritten = RewriteMethodReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    "SubsystemPlayers",
                    "get_PlayerAdded",
                    "get_LegacyPlayerAdded",
                    "System.Action`1<Game.PlayerData>",
                    [],
                    hasThis: true);
                rewritten = RewriteMethodReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    "SubsystemPlayers",
                    "set_PlayerAdded",
                    "set_LegacyPlayerAdded",
                    "System.Void",
                    ["System.Action`1<Game.PlayerData>"],
                    hasThis: true);
                rewritten = RewriteMethodReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    "SubsystemPlayers",
                    "get_PlayerRemoved",
                    "get_LegacyPlayerRemoved",
                    "System.Action`1<Game.PlayerData>",
                    [],
                    hasThis: true);
                rewritten = RewriteMethodReferenceName(
                    rewritten,
                    assemblyFileName,
                    SurvivalcraftAssemblyName,
                    "Game",
                    "SubsystemPlayers",
                    "set_PlayerRemoved",
                    "set_LegacyPlayerRemoved",
                    "System.Void",
                    ["System.Action`1<Game.PlayerData>"],
                    hasThis: true);
                applied.Add(FirearmsRemapPlayerCallbacksBreakId);
            }

            appliedBreakIds = applied.ToArray();
            return rewritten;
        }

        static byte[] RewriteOldRecipesGetter(
            string assemblyFileName,
            byte[] assemblyBytes,
            out bool rewrittenAssembly) {
            rewrittenAssembly = false;
            using MemoryStream input = new(assemblyBytes, false);
            using PEReader peReader = new(input, PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata) {
                return assemblyBytes;
            }
            MetadataReader reader = peReader.GetMetadataReader();
            HashSet<int> nameHeapOffsets = FindOldRecipesGetterNameOffsets(reader);
            if (nameHeapOffsets.Count == 0) {
                return assemblyBytes;
            }
            if (peReader.PEHeaders.CorHeader?.StrongNameSignatureDirectory.Size > 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            int stringsHeapFileOffset = FindStringsHeapFileOffset(
                peReader.PEHeaders,
                assemblyBytes);
            byte[] rewritten = (byte[])assemblyBytes.Clone();
            byte[] oldName = Encoding.ASCII.GetBytes(OldRecipesGetterName);
            byte[] newName = Encoding.ASCII.GetBytes(CompatibilityRecipesMethodName);
            if (oldName.Length != newName.Length) {
                throw new InvalidOperationException(
                    "Legacy metadata replacement names must have identical byte lengths.");
            }
            foreach (int heapOffset in nameHeapOffsets) {
                int fileOffset = checked(stringsHeapFileOffset + heapOffset);
                if (fileOffset < 0
                    || (long)fileOffset + oldName.Length >= rewritten.Length
                    || rewritten[fileOffset + oldName.Length] != 0
                    || !rewritten.AsSpan(fileOffset, oldName.Length).SequenceEqual(oldName)) {
                    throw new BadImageFormatException(
                        $"Legacy member name has an invalid #Strings location in '{assemblyFileName}'.");
                }
                newName.CopyTo(rewritten, fileOffset);
            }

            rewrittenAssembly = true;
            return rewritten;
        }

        internal static byte[] RewriteLegacyParticleSystemCalls(
            string assemblyFileName,
            byte[] assemblyBytes,
            out int rewrittenReferenceCount) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            using MemoryStream input = new(assemblyBytes, false);
            using Mono.Cecil.AssemblyDefinition assembly =
                Mono.Cecil.AssemblyDefinition.ReadAssembly(
                    input,
                    new Mono.Cecil.ReaderParameters {
                        InMemory = true,
                        ReadSymbols = false
                    });
            Mono.Cecil.MethodReference[] matches = assembly.MainModule
                .GetMemberReferences()
                .OfType<Mono.Cecil.MethodReference>()
                .Where(reference =>
                    string.Equals(reference.DeclaringType.Scope?.Name,
                        SurvivalcraftAssemblyName,
                        StringComparison.Ordinal)
                    && string.Equals(reference.DeclaringType.Namespace,
                        "Game",
                        StringComparison.Ordinal)
                    && string.Equals(reference.DeclaringType.Name,
                        SubsystemParticlesType,
                        StringComparison.Ordinal)
                    && reference.HasThis
                    && string.Equals(reference.ReturnType.FullName,
                        "System.Void",
                        StringComparison.Ordinal)
                    && reference.Parameters.Count == 1
                    && string.Equals(reference.Parameters[0].ParameterType.FullName,
                        ParticleSystemBaseType,
                        StringComparison.Ordinal)
                    && (string.Equals(reference.Name,
                            LegacyAddParticleSystemMethod,
                            StringComparison.Ordinal)
                        || string.Equals(reference.Name,
                            LegacyRemoveParticleSystemMethod,
                            StringComparison.Ordinal)))
                .ToArray();
            rewrittenReferenceCount = matches.Length;
            if (matches.Length == 0) {
                return assemblyBytes;
            }
            if (assembly.Name.HasPublicKey
                || (assembly.MainModule.Attributes
                    & Mono.Cecil.ModuleAttributes.StrongNameSigned) != 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            foreach (Mono.Cecil.MethodReference reference in matches) {
                reference.Name = string.Equals(reference.Name,
                    LegacyAddParticleSystemMethod,
                    StringComparison.Ordinal)
                    ? CompatibilityAddParticleSystemMethod
                    : CompatibilityRemoveParticleSystemMethod;
            }

            using MemoryStream output = new();
            assembly.Write(output);
            return output.ToArray();
        }

        internal static bool IsExactFirearms28Assembly(
            ModInfo modInfo,
            string assemblyFileName,
            byte[] assemblyBytes,
            string sourcePackageSha256) {
            if (!IsExactFirearms28Package(modInfo, sourcePackageSha256)
                || !string.Equals(assemblyFileName, Firearms28AssemblyName, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            string sha256 = Convert.ToHexString(SHA256.HashData(assemblyBytes));
            return string.Equals(sha256, Firearms28AssemblySha256, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsExactFirearms28Package(
            ModInfo modInfo,
            string sourcePackageSha256) =>
            modInfo != null
            && string.Equals(modInfo.PackageName, Firearms28PackageName, StringComparison.Ordinal)
            && string.Equals(modInfo.Version, Firearms28Version, StringComparison.Ordinal)
            && string.Equals(modInfo.ApiVersion, Firearms28ApiVersion, StringComparison.Ordinal)
            && string.Equals(
                sourcePackageSha256,
                Firearms28PackageSha256,
                StringComparison.OrdinalIgnoreCase);

        internal static byte[] RewriteTypeReferenceNamespace(
            byte[] assemblyBytes,
            string assemblyFileName,
            string assemblyScopeName,
            string oldNamespace,
            string typeName,
            string newNamespace) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            using MemoryStream input = new(assemblyBytes, false);
            using Mono.Cecil.AssemblyDefinition assembly =
                Mono.Cecil.AssemblyDefinition.ReadAssembly(
                    input,
                    new Mono.Cecil.ReaderParameters {
                        InMemory = true,
                        ReadSymbols = false
                    });
            if (assembly.Name.HasPublicKey
                || (assembly.MainModule.Attributes
                    & Mono.Cecil.ModuleAttributes.StrongNameSigned) != 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            Mono.Cecil.TypeReference[] matches = assembly.MainModule
                .GetTypeReferences()
                .Where(reference =>
                    string.Equals(reference.Scope?.Name, assemblyScopeName, StringComparison.Ordinal)
                    && string.Equals(reference.Namespace, oldNamespace, StringComparison.Ordinal)
                    && string.Equals(reference.Name, typeName, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1) {
                throw new BadImageFormatException(
                    $"Expected exactly one '{oldNamespace}.{typeName}' type reference "
                    + $"from '{assemblyScopeName}' in '{assemblyFileName}', found {matches.Length}.");
            }
            matches[0].Namespace = newNamespace;

            using MemoryStream output = new();
            assembly.Write(output);
            return output.ToArray();
        }

        internal static byte[] RewriteMethodReferenceName(
            byte[] assemblyBytes,
            string assemblyFileName,
            string assemblyScopeName,
            string declaringNamespace,
            string declaringTypeName,
            string oldMethodName,
            string newMethodName,
            string returnTypeName,
            IReadOnlyList<string> parameterTypeNames,
            bool hasThis = false) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            using MemoryStream input = new(assemblyBytes, false);
            using Mono.Cecil.AssemblyDefinition assembly =
                Mono.Cecil.AssemblyDefinition.ReadAssembly(
                    input,
                    new Mono.Cecil.ReaderParameters {
                        InMemory = true,
                        ReadSymbols = false
                    });
            if (assembly.Name.HasPublicKey
                || (assembly.MainModule.Attributes
                    & Mono.Cecil.ModuleAttributes.StrongNameSigned) != 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            Mono.Cecil.MethodReference[] matches = assembly.MainModule
                .GetMemberReferences()
                .OfType<Mono.Cecil.MethodReference>()
                .Where(reference =>
                    string.Equals(reference.DeclaringType.Scope?.Name,
                        assemblyScopeName,
                        StringComparison.Ordinal)
                    && string.Equals(reference.DeclaringType.Namespace,
                        declaringNamespace,
                        StringComparison.Ordinal)
                    && string.Equals(reference.DeclaringType.Name,
                        declaringTypeName,
                        StringComparison.Ordinal)
                    && string.Equals(reference.Name, oldMethodName,
                        StringComparison.Ordinal)
                    && reference.HasThis == hasThis
                    && string.Equals(reference.ReturnType.FullName,
                        returnTypeName,
                        StringComparison.Ordinal)
                    && reference.Parameters.Select(parameter => parameter.ParameterType.FullName)
                        .SequenceEqual(parameterTypeNames))
                .ToArray();
            if (matches.Length != 1) {
                throw new BadImageFormatException(
                    $"Expected exactly one '{declaringNamespace}.{declaringTypeName}."
                    + $"{oldMethodName}' method reference from '{assemblyScopeName}' in "
                    + $"'{assemblyFileName}', found {matches.Length}.");
            }
            matches[0].Name = newMethodName;

            using MemoryStream output = new();
            assembly.Write(output);
            return output.ToArray();
        }

        internal static byte[] RewriteFieldReferenceName(
            byte[] assemblyBytes,
            string assemblyFileName,
            string assemblyScopeName,
            string declaringNamespace,
            string declaringTypeName,
            string oldFieldName,
            string newFieldName,
            string fieldTypeName) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            using MemoryStream input = new(assemblyBytes, false);
            using Mono.Cecil.AssemblyDefinition assembly =
                Mono.Cecil.AssemblyDefinition.ReadAssembly(
                    input,
                    new Mono.Cecil.ReaderParameters {
                        InMemory = true,
                        ReadSymbols = false
                    });
            if (assembly.Name.HasPublicKey
                || (assembly.MainModule.Attributes
                    & Mono.Cecil.ModuleAttributes.StrongNameSigned) != 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            Mono.Cecil.FieldReference[] matches = assembly.MainModule
                .GetMemberReferences()
                .OfType<Mono.Cecil.FieldReference>()
                .Where(reference =>
                    string.Equals(reference.DeclaringType.Scope?.Name,
                        assemblyScopeName,
                        StringComparison.Ordinal)
                    && string.Equals(reference.DeclaringType.Namespace,
                        declaringNamespace,
                        StringComparison.Ordinal)
                    && string.Equals(reference.DeclaringType.Name,
                        declaringTypeName,
                        StringComparison.Ordinal)
                    && string.Equals(reference.Name, oldFieldName,
                        StringComparison.Ordinal)
                    && string.Equals(reference.FieldType.FullName,
                        fieldTypeName,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1) {
                throw new BadImageFormatException(
                    $"Expected exactly one '{declaringNamespace}.{declaringTypeName}."
                    + $"{oldFieldName}' field reference from '{assemblyScopeName}' in "
                    + $"'{assemblyFileName}', found {matches.Length}.");
            }
            matches[0].Name = newFieldName;

            using MemoryStream output = new();
            assembly.Write(output);
            return output.ToArray();
        }

        internal static byte[] RewriteStaticVoidCallToNops(
            byte[] assemblyBytes,
            string assemblyFileName,
            string callerTypeName,
            string callerMethodName,
            int callIlOffset,
            string targetTypeName,
            string targetMethodName) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            using MemoryStream input = new(assemblyBytes, false);
            using PEReader peReader = new(input, PEStreamOptions.PrefetchEntireImage);
            if (!peReader.HasMetadata) {
                throw new BadImageFormatException(
                    $"Legacy assembly '{assemblyFileName}' has no CLR metadata.");
            }
            if (peReader.PEHeaders.CorHeader?.StrongNameSignatureDirectory.Size > 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            MethodDefinitionHandle callerHandle = FindParameterlessVoidMethod(
                reader,
                callerTypeName,
                callerMethodName,
                requireStatic: false);
            MethodDefinition caller = reader.GetMethodDefinition(callerHandle);
            MethodBodyBlock body = peReader.GetMethodBody(caller.RelativeVirtualAddress);
            byte[] il = body.GetILBytes();
            if (callIlOffset < 0 || (long)callIlOffset + 5 > il.Length || il[callIlOffset] != 0x28) {
                throw new BadImageFormatException(
                    $"Expected call at IL_0x{callIlOffset:X4} in "
                    + $"'{callerTypeName}.{callerMethodName}' of '{assemblyFileName}'.");
            }

            int targetToken = BinaryPrimitives.ReadInt32LittleEndian(
                il.AsSpan(callIlOffset + 1, 4));
            EntityHandle targetHandle = MetadataTokens.EntityHandle(targetToken);
            if (targetHandle.Kind != HandleKind.MethodDefinition) {
                throw new BadImageFormatException(
                    $"Expected a method-definition call token at IL_0x{callIlOffset:X4} in "
                    + $"'{callerTypeName}.{callerMethodName}' of '{assemblyFileName}'.");
            }
            MethodDefinition target = reader.GetMethodDefinition((MethodDefinitionHandle)targetHandle);
            MethodSignature<string> targetSignature = target.DecodeSignature(
                SignatureNameProvider.Instance,
                genericContext: null);
            if (GetTypeDefinitionName(reader, target.GetDeclaringType()) != targetTypeName
                || reader.GetString(target.Name) != targetMethodName
                || (target.Attributes & MethodAttributes.Static) == 0
                || targetSignature.ParameterTypes.Length != 0
                || targetSignature.ReturnType != VoidTypeName) {
                throw new BadImageFormatException(
                    $"Call at IL_0x{callIlOffset:X4} in '{callerTypeName}.{callerMethodName}' "
                    + $"does not target '{targetTypeName}.{targetMethodName}()' in "
                    + $"'{assemblyFileName}'.");
            }

            int methodBodyFileOffset = RvaToFileOffset(
                peReader.PEHeaders,
                caller.RelativeVirtualAddress);
            int ilFileOffset = GetMethodIlFileOffset(
                assemblyBytes,
                methodBodyFileOffset,
                il.Length,
                assemblyFileName);
            int callFileOffset = checked(ilFileOffset + callIlOffset);
            if ((long)callFileOffset + 5 > assemblyBytes.Length
                || !assemblyBytes.AsSpan(callFileOffset, 5)
                    .SequenceEqual(il.AsSpan(callIlOffset, 5))) {
                throw new BadImageFormatException(
                    $"Method body location for '{callerTypeName}.{callerMethodName}' is invalid "
                    + $"in '{assemblyFileName}'.");
            }

            byte[] rewritten = (byte[])assemblyBytes.Clone();
            rewritten.AsSpan(callFileOffset, 5).Clear();
            return rewritten;
        }

        internal static byte[] RewriteInstanceVoidMethodToReturn(
            byte[] assemblyBytes,
            string assemblyFileName,
            string typeName,
            string methodName,
            IReadOnlyList<string> parameterTypes,
            int expectedCodeSize) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            using MemoryStream input = new(assemblyBytes, false);
            using PEReader peReader = new(input, PEStreamOptions.PrefetchEntireImage);
            if (!peReader.HasMetadata) {
                throw new BadImageFormatException(
                    $"Legacy assembly '{assemblyFileName}' has no CLR metadata.");
            }
            if (peReader.PEHeaders.CorHeader?.StrongNameSignatureDirectory.Size > 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            MethodDefinitionHandle methodHandle = FindMethod(
                reader,
                typeName,
                methodName,
                VoidTypeName,
                parameterTypes,
                requireStatic: false);
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.Static) != 0) {
                throw new BadImageFormatException(
                    $"Expected instance method '{typeName}.{methodName}' in '{assemblyFileName}'.");
            }
            MethodBodyBlock body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            byte[] il = body.GetILBytes();
            if (il.Length != expectedCodeSize) {
                throw new BadImageFormatException(
                    $"Method '{typeName}.{methodName}' in '{assemblyFileName}' has IL size "
                    + $"0x{il.Length:X}, expected 0x{expectedCodeSize:X}.");
            }

            int methodBodyFileOffset = RvaToFileOffset(
                peReader.PEHeaders,
                method.RelativeVirtualAddress);
            int ilFileOffset = GetMethodIlFileOffset(
                assemblyBytes,
                methodBodyFileOffset,
                expectedCodeSize,
                assemblyFileName);
            byte[] rewritten = (byte[])assemblyBytes.Clone();
            byte first = rewritten[methodBodyFileOffset];
            switch (first & 0x3) {
                case 0x2:
                    rewritten[methodBodyFileOffset] = 0x06;
                    rewritten[ilFileOffset] = 0x2A;
                    break;
                case 0x3:
                    ushort flagsAndSize = BinaryPrimitives.ReadUInt16LittleEndian(
                        rewritten.AsSpan(methodBodyFileOffset, 2));
                    int headerDwords = flagsAndSize >> 12;
                    if (headerDwords < 3) {
                        throw new BadImageFormatException(
                            $"Method '{typeName}.{methodName}' has an invalid fat header "
                            + $"in '{assemblyFileName}'.");
                    }
                    const ushort moreSectionsFlag = 0x0008;
                    const ushort initLocalsFlag = 0x0010;
                    flagsAndSize = (ushort)(flagsAndSize
                        & ~moreSectionsFlag
                        & ~initLocalsFlag);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        rewritten.AsSpan(methodBodyFileOffset, 2),
                        flagsAndSize);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        rewritten.AsSpan(methodBodyFileOffset + 2, 2),
                        1);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        rewritten.AsSpan(methodBodyFileOffset + 4, 4),
                        1);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        rewritten.AsSpan(methodBodyFileOffset + 8, 4),
                        0);
                    rewritten[ilFileOffset] = 0x2A;
                    break;
                default:
                    throw new BadImageFormatException(
                        $"Method '{typeName}.{methodName}' has an invalid body header "
                        + $"in '{assemblyFileName}'.");
            }
            return rewritten;
        }

        internal static byte[] RewriteStaticVoidCallToPops(
            byte[] assemblyBytes,
            string assemblyFileName,
            string callerTypeName,
            string callerMethodName,
            IReadOnlyList<string> callerParameterTypes,
            int callIlOffset,
            string targetTypeName,
            string targetMethodName,
            IReadOnlyList<string> targetParameterTypes) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            if (targetParameterTypes.Count is < 1 or > 5) {
                throw new ArgumentOutOfRangeException(
                    nameof(targetParameterTypes),
                    "A five-byte call site can replace one to five arguments with pop opcodes.");
            }
            using MemoryStream input = new(assemblyBytes, false);
            using PEReader peReader = new(input, PEStreamOptions.PrefetchEntireImage);
            if (!peReader.HasMetadata) {
                throw new BadImageFormatException(
                    $"Legacy assembly '{assemblyFileName}' has no CLR metadata.");
            }
            if (peReader.PEHeaders.CorHeader?.StrongNameSignatureDirectory.Size > 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            MethodDefinitionHandle callerHandle = FindMethod(
                reader,
                callerTypeName,
                callerMethodName,
                VoidTypeName,
                callerParameterTypes,
                requireStatic: false);
            MethodDefinition caller = reader.GetMethodDefinition(callerHandle);
            if ((caller.Attributes & MethodAttributes.Static) != 0) {
                throw new BadImageFormatException(
                    $"Expected instance method '{callerTypeName}.{callerMethodName}' "
                    + $"in '{assemblyFileName}'.");
            }
            MethodBodyBlock body = peReader.GetMethodBody(caller.RelativeVirtualAddress);
            byte[] il = body.GetILBytes();
            if (callIlOffset < 0 || (long)callIlOffset + 5 > il.Length
                || il[callIlOffset] != 0x28) {
                throw new BadImageFormatException(
                    $"Expected call at IL_0x{callIlOffset:X4} in "
                    + $"'{callerTypeName}.{callerMethodName}' of '{assemblyFileName}'.");
            }

            int targetToken = BinaryPrimitives.ReadInt32LittleEndian(
                il.AsSpan(callIlOffset + 1, 4));
            EntityHandle targetHandle = MetadataTokens.EntityHandle(targetToken);
            if (targetHandle.Kind != HandleKind.MethodDefinition) {
                throw new BadImageFormatException(
                    $"Expected a method-definition call token at IL_0x{callIlOffset:X4} in "
                    + $"'{callerTypeName}.{callerMethodName}' of '{assemblyFileName}'.");
            }
            MethodDefinition target = reader.GetMethodDefinition((MethodDefinitionHandle)targetHandle);
            MethodSignature<string> targetSignature = target.DecodeSignature(
                SignatureNameProvider.Instance,
                genericContext: null);
            if (GetTypeDefinitionName(reader, target.GetDeclaringType()) != targetTypeName
                || reader.GetString(target.Name) != targetMethodName
                || (target.Attributes & MethodAttributes.Static) == 0
                || targetSignature.ReturnType != VoidTypeName
                || !targetSignature.ParameterTypes.SequenceEqual(targetParameterTypes)) {
                throw new BadImageFormatException(
                    $"Call at IL_0x{callIlOffset:X4} in '{callerTypeName}.{callerMethodName}' "
                    + $"does not target the verified '{targetTypeName}.{targetMethodName}' "
                    + $"signature in '{assemblyFileName}'.");
            }

            int methodBodyFileOffset = RvaToFileOffset(
                peReader.PEHeaders,
                caller.RelativeVirtualAddress);
            int ilFileOffset = GetMethodIlFileOffset(
                assemblyBytes,
                methodBodyFileOffset,
                il.Length,
                assemblyFileName);
            int callFileOffset = checked(ilFileOffset + callIlOffset);
            byte[] rewritten = (byte[])assemblyBytes.Clone();
            rewritten.AsSpan(callFileOffset, 5).Clear();
            rewritten.AsSpan(callFileOffset, targetParameterTypes.Count).Fill(0x26);
            return rewritten;
        }

        internal static byte[] RewriteTwoArgumentStaticCallToNewObject(
            byte[] assemblyBytes,
            string assemblyFileName,
            string callerTypeName,
            string callerMethodName,
            int sequenceIlOffset,
            string targetTypeName,
            string targetMethodName,
            string targetReturnType,
            string[] targetParameterTypes) {
            ArgumentNullException.ThrowIfNull(assemblyBytes);
            using MemoryStream input = new(assemblyBytes, false);
            using PEReader peReader = new(input, PEStreamOptions.PrefetchEntireImage);
            if (!peReader.HasMetadata) {
                throw new BadImageFormatException(
                    $"Legacy assembly '{assemblyFileName}' has no CLR metadata.");
            }
            if (peReader.PEHeaders.CorHeader?.StrongNameSignatureDirectory.Size > 0) {
                throw new InvalidOperationException(
                    $"Cannot safely rewrite strong-named legacy assembly '{assemblyFileName}'.");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            MethodDefinitionHandle callerHandle = FindParameterlessVoidMethod(
                reader,
                callerTypeName,
                callerMethodName,
                requireStatic: true);
            MethodDefinitionHandle targetHandle = FindMethod(
                reader,
                targetTypeName,
                targetMethodName,
                targetReturnType,
                targetParameterTypes,
                requireStatic: true);
            MethodDefinition caller = reader.GetMethodDefinition(callerHandle);
            MethodBodyBlock callerBody = peReader.GetMethodBody(caller.RelativeVirtualAddress);
            byte[] callerIl = callerBody.GetILBytes();
            if (sequenceIlOffset < 0
                || (long)sequenceIlOffset + 15 > callerIl.Length
                || callerIl[sequenceIlOffset] != 0x72
                || callerIl[sequenceIlOffset + 5] != 0x7E
                || callerIl[sequenceIlOffset + 10] != 0x28) {
                throw new BadImageFormatException(
                    $"Expected 'ldstr; ldsfld; call' at IL_0x{sequenceIlOffset:X4} in "
                    + $"'{callerTypeName}.{callerMethodName}' of '{assemblyFileName}'.");
            }
            int calledToken = BinaryPrimitives.ReadInt32LittleEndian(
                callerIl.AsSpan(sequenceIlOffset + 11, 4));
            if (MetadataTokens.EntityHandle(calledToken) != targetHandle) {
                throw new BadImageFormatException(
                    $"Call at IL_0x{sequenceIlOffset + 10:X4} in "
                    + $"'{callerTypeName}.{callerMethodName}' does not target the verified "
                    + $"'{targetTypeName}.{targetMethodName}' method.");
            }

            MethodDefinition target = reader.GetMethodDefinition(targetHandle);
            MethodBodyBlock targetBody = peReader.GetMethodBody(target.RelativeVirtualAddress);
            byte[] targetIl = targetBody.GetILBytes();
            if (targetIl.Length < 5 || targetIl[0] != 0x73) {
                throw new BadImageFormatException(
                    $"Expected '{targetTypeName}.{targetMethodName}' in '{assemblyFileName}' "
                    + "to construct its result list as its first instruction.");
            }
            int constructorToken = BinaryPrimitives.ReadInt32LittleEndian(
                targetIl.AsSpan(1, 4));
            EntityHandle constructorHandle = MetadataTokens.EntityHandle(constructorToken);
            if (constructorHandle.Kind != HandleKind.MemberReference) {
                throw new BadImageFormatException(
                    $"Expected a member-reference constructor token in "
                    + $"'{targetTypeName}.{targetMethodName}' of '{assemblyFileName}'.");
            }
            MemberReference constructor = reader.GetMemberReference(
                (MemberReferenceHandle)constructorHandle);
            MethodSignature<string> constructorSignature = constructor.DecodeMethodSignature(
                SignatureNameProvider.Instance,
                genericContext: null);
            if (reader.GetString(constructor.Name) != ".ctor"
                || GetMemberReferenceParentTypeName(reader, constructor.Parent) != targetReturnType
                || constructorSignature.ParameterTypes.Length != 0
                || constructorSignature.ReturnType != VoidTypeName) {
                throw new BadImageFormatException(
                    $"The result constructor in '{targetTypeName}.{targetMethodName}' of "
                    + $"'{assemblyFileName}' does not match '{targetReturnType}..ctor()'.");
            }

            int callerBodyFileOffset = RvaToFileOffset(
                peReader.PEHeaders,
                caller.RelativeVirtualAddress);
            int ilFileOffset = GetMethodIlFileOffset(
                assemblyBytes,
                callerBodyFileOffset,
                callerIl.Length,
                assemblyFileName);
            byte[] rewritten = (byte[])assemblyBytes.Clone();
            int sequenceFileOffset = checked(ilFileOffset + sequenceIlOffset);
            rewritten[sequenceFileOffset] = 0x73;
            BinaryPrimitives.WriteInt32LittleEndian(
                rewritten.AsSpan(sequenceFileOffset + 1, 4),
                constructorToken);
            rewritten.AsSpan(sequenceFileOffset + 5, 10).Clear();
            return rewritten;
        }

        internal static bool IsPre181ApiVersion(string apiVersion) {
            if (string.IsNullOrWhiteSpace(apiVersion)) {
                return false;
            }
            string normalized = apiVersion.Trim();
            if (normalized.StartsWith("1.4", StringComparison.Ordinal)
                || normalized.StartsWith("1.5", StringComparison.Ordinal)
                || normalized.StartsWith("1.6", StringComparison.Ordinal)
                || normalized.StartsWith("1.7", StringComparison.Ordinal)) {
                return true;
            }
            return normalized is "1.8" or "1.80"
                || normalized.StartsWith("1.8.0", StringComparison.Ordinal);
        }

        static MethodDefinitionHandle FindParameterlessVoidMethod(
            MetadataReader reader,
            string typeName,
            string methodName,
            bool requireStatic) {
            MethodDefinitionHandle found = default;
            int matches = 0;
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions) {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                if (GetTypeDefinitionName(reader, typeHandle) != typeName) {
                    continue;
                }
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods()) {
                    MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                    if (reader.GetString(method.Name) != methodName
                        || (requireStatic && (method.Attributes & MethodAttributes.Static) == 0)) {
                        continue;
                    }
                    MethodSignature<string> signature = method.DecodeSignature(
                        SignatureNameProvider.Instance,
                        genericContext: null);
                    if (signature.ParameterTypes.Length == 0
                        && signature.ReturnType == VoidTypeName) {
                        found = methodHandle;
                        matches++;
                    }
                }
            }
            if (matches != 1) {
                throw new BadImageFormatException(
                    $"Expected exactly one parameterless void method '{typeName}.{methodName}', "
                    + $"but found {matches}.");
            }
            return found;
        }

        static MethodDefinitionHandle FindMethod(
            MetadataReader reader,
            string typeName,
            string methodName,
            string returnType,
            IReadOnlyList<string> parameterTypes,
            bool requireStatic) {
            MethodDefinitionHandle found = default;
            int matches = 0;
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions) {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                if (GetTypeDefinitionName(reader, typeHandle) != typeName) {
                    continue;
                }
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods()) {
                    MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                    if (reader.GetString(method.Name) != methodName
                        || (requireStatic && (method.Attributes & MethodAttributes.Static) == 0)) {
                        continue;
                    }
                    MethodSignature<string> signature = method.DecodeSignature(
                        SignatureNameProvider.Instance,
                        genericContext: null);
                    if (signature.ReturnType == returnType
                        && signature.ParameterTypes.SequenceEqual(parameterTypes)) {
                        found = methodHandle;
                        matches++;
                    }
                }
            }
            if (matches != 1) {
                throw new BadImageFormatException(
                    $"Expected exactly one method '{typeName}.{methodName}' with the verified "
                    + $"signature, but found {matches}.");
            }
            return found;
        }

        static int GetMethodIlFileOffset(
            byte[] assemblyBytes,
            int methodBodyFileOffset,
            int expectedCodeSize,
            string assemblyFileName) {
            if (methodBodyFileOffset < 0 || methodBodyFileOffset >= assemblyBytes.Length) {
                throw new BadImageFormatException(
                    $"Method body is outside '{assemblyFileName}'.");
            }
            byte first = assemblyBytes[methodBodyFileOffset];
            int headerSize;
            int codeSize;
            switch (first & 0x3) {
                case 0x2:
                    headerSize = 1;
                    codeSize = first >> 2;
                    break;
                case 0x3:
                    if ((long)methodBodyFileOffset + 12 > assemblyBytes.Length) {
                        throw new BadImageFormatException(
                            $"Method body has a truncated fat header in '{assemblyFileName}'.");
                    }
                    ushort flagsAndSize = BinaryPrimitives.ReadUInt16LittleEndian(
                        assemblyBytes.AsSpan(methodBodyFileOffset, 2));
                    int headerDwords = flagsAndSize >> 12;
                    headerSize = checked(headerDwords * 4);
                    codeSize = BinaryPrimitives.ReadInt32LittleEndian(
                        assemblyBytes.AsSpan(methodBodyFileOffset + 4, 4));
                    if (headerDwords < 3) {
                        throw new BadImageFormatException(
                            $"Method body has an invalid fat header in '{assemblyFileName}'.");
                    }
                    break;
                default:
                    throw new BadImageFormatException(
                        $"Method body has an invalid header in '{assemblyFileName}'.");
            }
            int ilFileOffset = checked(methodBodyFileOffset + headerSize);
            if (codeSize != expectedCodeSize
                || codeSize < 0
                || (long)ilFileOffset + codeSize > assemblyBytes.Length) {
                throw new BadImageFormatException(
                    $"Method body has an invalid IL extent in '{assemblyFileName}'.");
            }
            return ilFileOffset;
        }

        static HashSet<int> FindOldRecipesGetterNameOffsets(MetadataReader reader) {
            HashSet<int> offsets = [];
            foreach (MemberReferenceHandle handle in reader.MemberReferences) {
                MemberReference member = reader.GetMemberReference(handle);
                if (member.GetKind() != MemberReferenceKind.Method
                    || reader.GetString(member.Name) != OldRecipesGetterName
                    || GetDeclaringTypeName(reader, member.Parent) != CraftingRecipesManagerType) {
                    continue;
                }
                MethodSignature<string> signature = member.DecodeMethodSignature(
                    SignatureNameProvider.Instance,
                    genericContext: null);
                if (signature.ParameterTypes.Length == 0
                    && signature.ReturnType == OldRecipesReturnType) {
                    offsets.Add(MetadataTokens.GetHeapOffset(member.Name));
                }
            }
            return offsets;
        }

        static string GetDeclaringTypeName(MetadataReader reader, EntityHandle handle) {
            if (handle.Kind != HandleKind.TypeReference) {
                return string.Empty;
            }
            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)handle);
            string typeNamespace = reader.GetString(type.Namespace);
            string typeName = reader.GetString(type.Name);
            return string.IsNullOrEmpty(typeNamespace)
                ? typeName
                : $"{typeNamespace}.{typeName}";
        }

        static string GetMemberReferenceParentTypeName(
            MetadataReader reader,
            EntityHandle handle) => handle.Kind switch {
                HandleKind.TypeDefinition => GetTypeDefinitionName(
                    reader,
                    (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => GetTypeReferenceName(
                    reader,
                    (TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => reader.GetTypeSpecification(
                        (TypeSpecificationHandle)handle)
                    .DecodeSignature(SignatureNameProvider.Instance, genericContext: null),
                _ => string.Empty
            };

        static string GetTypeReferenceName(
            MetadataReader reader,
            TypeReferenceHandle handle) {
            TypeReference type = reader.GetTypeReference(handle);
            string typeNamespace = reader.GetString(type.Namespace);
            string typeName = reader.GetString(type.Name);
            return string.IsNullOrEmpty(typeNamespace)
                ? typeName
                : $"{typeNamespace}.{typeName}";
        }

        static string GetTypeDefinitionName(
            MetadataReader reader,
            TypeDefinitionHandle handle) {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            string typeNamespace = reader.GetString(type.Namespace);
            string typeName = reader.GetString(type.Name);
            return string.IsNullOrEmpty(typeNamespace)
                ? typeName
                : $"{typeNamespace}.{typeName}";
        }

        static int FindStringsHeapFileOffset(PEHeaders headers, byte[] assemblyBytes) {
            DirectoryEntry metadataDirectory = headers.CorHeader?.MetadataDirectory
                ?? throw new BadImageFormatException("Assembly has no CLR metadata directory.");
            int metadataOffset = RvaToFileOffset(
                headers,
                metadataDirectory.RelativeVirtualAddress);
            ReadOnlySpan<byte> bytes = assemblyBytes;
            if (metadataOffset < 0
                || metadataOffset + 20 > bytes.Length
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(metadataOffset, 4))
                    != 0x424A5342) {
                throw new BadImageFormatException("Assembly has an invalid CLR metadata root.");
            }

            int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(metadataOffset + 12, 4));
            if (versionLength < 0) {
                throw new BadImageFormatException("Assembly has an invalid CLR metadata version header.");
            }
            int cursor = checked(metadataOffset + 16 + versionLength);
            cursor = checked((cursor + 3) & ~3);
            if (cursor < 0 || (long)cursor + 4 > bytes.Length) {
                throw new BadImageFormatException("Assembly has an invalid CLR metadata version header.");
            }
            ushort streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.Slice(cursor + 2, 2));
            cursor += 4;
            for (int index = 0; index < streamCount; index++) {
                if (cursor + 8 > bytes.Length) {
                    throw new BadImageFormatException("Assembly has a truncated metadata stream header.");
                }
                int streamOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(cursor, 4)));
                int nameStart = cursor + 8;
                int nameEnd = nameStart;
                while (nameEnd < bytes.Length && bytes[nameEnd] != 0) {
                    nameEnd++;
                }
                if (nameEnd >= bytes.Length) {
                    throw new BadImageFormatException("Assembly has an unterminated metadata stream name.");
                }
                string streamName = Encoding.ASCII.GetString(bytes.Slice(
                    nameStart,
                    nameEnd - nameStart));
                cursor = checked((nameEnd + 1 + 3) & ~3);
                if (streamName == "#Strings") {
                    return checked(metadataOffset + streamOffset);
                }
            }
            throw new BadImageFormatException("Assembly metadata does not contain a #Strings heap.");
        }

        static int RvaToFileOffset(PEHeaders headers, int rva) {
            foreach (SectionHeader section in headers.SectionHeaders) {
                int sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);
                if (rva >= section.VirtualAddress
                    && (long)rva < (long)section.VirtualAddress + sectionSize) {
                    return checked(section.PointerToRawData + rva - section.VirtualAddress);
                }
            }
            if (rva >= 0 && rva < headers.PEHeader?.SizeOfHeaders) {
                return rva;
            }
            throw new BadImageFormatException(
                $"CLR metadata RVA 0x{rva:X8} is outside all PE sections.");
        }

        sealed class SignatureNameProvider : ISignatureTypeProvider<string, object> {
            internal static readonly SignatureNameProvider Instance = new();

            public string GetArrayType(string elementType, ArrayShape shape) =>
                $"{elementType}[{new string(',', Math.Max(0, shape.Rank - 1))}]";

            public string GetByReferenceType(string elementType) => $"{elementType}&";

            public string GetFunctionPointerType(MethodSignature<string> signature) => "method";

            public string GetGenericInstantiation(
                string genericType,
                ImmutableArray<string> typeArguments) =>
                $"{genericType}<{string.Join(",", typeArguments)}>";

            public string GetGenericMethodParameter(object genericContext, int index) => $"!!{index}";

            public string GetGenericTypeParameter(object genericContext, int index) => $"!{index}";

            public string GetModifiedType(
                string modifierType,
                string unmodifiedType,
                bool isRequired) => unmodifiedType;

            public string GetPinnedType(string elementType) => elementType;

            public string GetPointerType(string elementType) => $"{elementType}*";

            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

            public string GetSZArrayType(string elementType) => $"{elementType}[]";

            public string GetTypeFromDefinition(
                MetadataReader reader,
                TypeDefinitionHandle handle,
                byte rawTypeKind) {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                return JoinTypeName(reader.GetString(type.Namespace), reader.GetString(type.Name));
            }

            public string GetTypeFromReference(
                MetadataReader reader,
                TypeReferenceHandle handle,
                byte rawTypeKind) {
                TypeReference type = reader.GetTypeReference(handle);
                return JoinTypeName(reader.GetString(type.Namespace), reader.GetString(type.Name));
            }

            public string GetTypeFromSpecification(
                MetadataReader reader,
                object genericContext,
                TypeSpecificationHandle handle,
                byte rawTypeKind) => reader.GetTypeSpecification(handle)
                    .DecodeSignature(this, genericContext);

            static string JoinTypeName(string typeNamespace, string typeName) =>
                string.IsNullOrEmpty(typeNamespace)
                    ? typeName
                    : $"{typeNamespace}.{typeName}";
        }
    }
}
