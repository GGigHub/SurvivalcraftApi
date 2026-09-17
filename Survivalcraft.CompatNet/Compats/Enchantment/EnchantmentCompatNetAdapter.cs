using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Conservative compatibility adapter for the Enchantment mod.
/// </summary>
public sealed class EnchantmentCompatNetAdapter : CompatNetAdapterLoader {
    public const ushort StableAdapterId = 5;
    public const string TargetPackageName = "Enchantment";

    private const string TargetAssemblyFileName = "Enchantment.dll";
    private bool m_targetLoaded;

    public override ushort AdapterId => StableAdapterId;
    public override string AdapterName => TargetPackageName;

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        m_targetLoaded = false;
        EnchantmentCompatNetRuntime.Reset();
        if (assemblies == null) {
            return;
        }

        foreach (ModAssemblyImage image in assemblies) {
            if (image == null
                || image.Owner?.modInfo == null
                || !string.Equals(
                    image.Owner.modInfo.PackageName,
                    TargetPackageName,
                    StringComparison.Ordinal)
                || !string.Equals(
                    image.FileName,
                    TargetAssemblyFileName,
                    StringComparison.Ordinal)) {
                continue;
            }

            m_targetLoaded = true;

            try {
                if (EnchantmentCompatNetPatcher.Patch(image)) {
                    Log.Information($"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                }
            }
            catch (Exception e) {
                // A third-party image must never prevent the remaining mods from loading.
                Log.Error($"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
            }
        }
    }

    public override void OnServerUpdate(float dt) {
        if (m_targetLoaded) {
            EnchantmentCompatNetRuntime.OnServerUpdate();
        }
    }

    public override bool ShouldSkipClientUpdate(IUpdateable updateable) {
        if (!m_targetLoaded
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || updateable == null
            || updateable.GetType().Assembly.GetName().Name != "Enchantment") {
            return false;
        }

        string name = updateable.GetType().Name;
        return updateable is Subsystem
            && (name.Contains("Walker", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Ground", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Magma", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Ice", StringComparison.OrdinalIgnoreCase));
    }

    public override void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (packet == null || packet.Payload == null) {
            return;
        }

        try {
            switch ((EnchantmentCompatNetOperation)packet.Operation) {
                case EnchantmentCompatNetOperation.RollEnchantment: {
                    EnchantmentRollRequest request = new();
                    CompatNetPayload.Read(packet.Payload, request.Serialize);
                    EnchantmentCompatNetRuntime.TryExecuteServerRoll(
                        request.EntityId,
                        playerData?.ComponentPlayer);
                    break;
                }
            }
        }
        catch (Exception e) {
            Log.Warning($"[Survivalcraft.CompatNet] Ignored malformed Enchantment request: {e.Message}");
        }
    }

    private static class EnchantmentCompatNetPatcher {
        private const string WidgetTypeName = "MagicRemovalPlatformWidget";
        private const string PlatformTypeName = "ComponentMagicRemovalPlatform";
        private const string RollMethodName = "RollEnchantment";
        private const string UpdateMethodName = "Update";

        public static bool Patch(ModAssemblyImage image) {
            if (image?.Bytes == null || image.Bytes.Length == 0) {
                return false;
            }

            using MemoryStream input = new(image.Bytes, writable: false);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                input,
                new ReaderParameters { InMemory = true, ReadSymbols = false });

            MethodInfo bridgeMethod = typeof(EnchantmentCompatNetRuntime).GetMethod(
                nameof(EnchantmentCompatNetRuntime.RollEnchantmentBridge),
                BindingFlags.Public | BindingFlags.Static);
            if (bridgeMethod == null) {
                return false;
            }

            MethodReference bridge = assembly.MainModule.ImportReference(bridgeMethod);
            MethodReference mendingGuard = ResolveBridge(
                assembly.MainModule,
                nameof(EnchantmentCompatNetRuntime.ShouldSkipMendingRepairUpdate));
            MethodReference fishingGuard = ResolveBridge(
                assembly.MainModule,
                nameof(EnchantmentCompatNetRuntime.ShouldSkipFishingFloatUpdate));
            MethodReference recallGuard = ResolveBridge(
                assembly.MainModule,
                nameof(EnchantmentCompatNetRuntime.ShouldSkipRecallPatch));
            bool changed = false;

            foreach (TypeDefinition widget in assembly.MainModule.Types
                         .SelectMany(FlattenTypes)
                         .Where(type => type.Name == WidgetTypeName)) {
                foreach (MethodDefinition update in widget.Methods.Where(
                             method => !method.IsStatic && method.Name == UpdateMethodName
                                 && method.HasBody)) {
                    foreach (Instruction instruction in update.Body.Instructions) {
                        if (!IsSupportedRollCall(instruction)) {
                            continue;
                        }

                        // Replacing the call itself leaves all other update logic
                        // untouched and lets the bridge decide by process mode.
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = bridge;
                        changed = true;
                    }
                }
            }

            changed |= InjectUpdateGuard(
                FindType(assembly, MendingRepairTypeName),
                mendingGuard);
            changed |= InjectUpdateGuard(
                FindType(assembly, FishingFloatsTypeName),
                fishingGuard);
            changed |= InjectRecallPrefixGuard(
                FindType(assembly, RecallPrefixTypeName),
                recallGuard);

            if (!changed) {
                return false;
            }

            using MemoryStream output = new();
            assembly.Write(output, new WriterParameters { WriteSymbols = false });
            image.Bytes = output.ToArray();
            return true;
        }

        private const string MendingRepairTypeName =
            "Enchantment.ENSubsystemMendingRepairSystem";
        private const string FishingFloatsTypeName =
            "Enchantment.ENSubsystemFishingFloats";
        private const string RecallPrefixTypeName =
            "Enchantment.Patches.EnchantmentPatches/RecallDiePatch";

        private static MethodReference ResolveBridge(ModuleDefinition module, string methodName) {
            MethodInfo method = typeof(EnchantmentCompatNetRuntime).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static);
            return module.ImportReference(method);
        }

        private static bool InjectUpdateGuard(
            TypeDefinition type,
            MethodReference guard) {
            if (type == null || guard == null) {
                return false;
            }

            MethodDefinition method = FlattenTypes(type)
                .SelectMany(item => item.Methods)
                .FirstOrDefault(item =>
                    !item.IsStatic
                    && item.Name == "Update"
                    && item.ReturnType.FullName == "System.Void"
                    && item.Parameters.Count == 1
                    && item.Parameters[0].ParameterType.FullName == "System.Single"
                    && item.HasBody);
            if (method == null || ContainsBridgeCall(method, guard.Name)) {
                return false;
            }

            ILProcessor il = method.Body.GetILProcessor();
            Instruction first = method.Body.Instructions[0];
            il.InsertBefore(first, Instruction.Create(OpCodes.Call, guard));
            il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
            return true;
        }

        private static bool InjectRecallPrefixGuard(
            TypeDefinition type,
            MethodReference guard) {
            if (type == null || guard == null) {
                return false;
            }

            MethodDefinition method = FlattenTypes(type)
                .SelectMany(item => item.Methods)
                .FirstOrDefault(item =>
                    item.IsStatic
                    && item.Name == "Prefix"
                    && item.ReturnType.FullName == "System.Boolean"
                    && item.HasBody);
            if (method == null || ContainsBridgeCall(method, guard.Name)) {
                return false;
            }

            ILProcessor il = method.Body.GetILProcessor();
            Instruction first = method.Body.Instructions[0];
            il.InsertBefore(first, Instruction.Create(OpCodes.Call, guard));
            il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
            return true;
        }

        private static bool ContainsBridgeCall(MethodDefinition method, string methodName) {
            return method.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference reference
                && reference.Name == methodName
                && reference.DeclaringType?.FullName == typeof(EnchantmentCompatNetRuntime).FullName);
        }

        private static TypeDefinition FindType(AssemblyDefinition assembly, string fullName) {
            return assembly.MainModule.Types
                .SelectMany(FlattenTypes)
                .FirstOrDefault(type => type.FullName == fullName);
        }

        private static bool IsSupportedRollCall(Instruction instruction) {
            if (instruction?.Operand is not MethodReference reference
                || reference.Name != RollMethodName
                || reference.DeclaringType?.Name != PlatformTypeName
                || !reference.HasThis
                || reference.Parameters.Count != 0
                || reference.ReturnType?.FullName != "System.Void") {
                return false;
            }

            // If the reference resolves, reject static or otherwise incompatible
            // definitions. Unresolvable references remain safe because the call
            // shape above is already conservative; Cecil failures are caught by
            // the adapter boundary.
            try {
                MethodDefinition resolved = reference.Resolve();
                return resolved == null || (!resolved.IsStatic && resolved.Parameters.Count == 0);
            }
            catch {
                // An optional dependency may be unavailable to Cecil while the
                // call shape itself remains strictly constrained above.
                return true;
            }
        }

        private static IEnumerable<TypeDefinition> FlattenTypes(TypeDefinition type) {
            yield return type;
            foreach (TypeDefinition nested in type.NestedTypes.SelectMany(FlattenTypes)) {
                yield return nested;
            }
        }
    }
}
