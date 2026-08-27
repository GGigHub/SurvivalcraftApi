using System.Reflection;
using Engine;
using Game.Network;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Compatibility adapter for the original ysf.neorxna mod.
/// </summary>
public sealed class NeorxnaCompatNetAdapter : CompatNetAdapterLoader, ICompatNetAdapter {
    public const ushort StableAdapterId = 4;
    public const string TargetPackageName = "ysf.neorxna";

    public override ushort AdapterId => StableAdapterId;
    public override string AdapterName => TargetPackageName;

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        foreach (ModAssemblyImage image in assemblies) {
            if (image == null
                || image.Owner?.modInfo == null
                || !string.Equals(
                    image.Owner.modInfo.PackageName,
                    TargetPackageName,
                    StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            try {
                if (NeorxnaCompatNetPatcher.Patch(image)) {
                    Log.Information($"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                }
            }
            catch (Exception e) {
                // A third-party image must not prevent the remaining mods from loading.
                Log.Error($"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
            }
        }
    }

    public override void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (packet == null) {
            return;
        }

        switch ((NeorxnaCompatNetOperation)packet.Operation) {
            case NeorxnaCompatNetOperation.StartDash:
                NeorxnaCompatNetRuntime.TryExecuteServerDash(
                    playerData?.ComponentPlayer,
                    active: true);
                break;
            case NeorxnaCompatNetOperation.StopDash:
                NeorxnaCompatNetRuntime.TryExecuteServerDash(
                    playerData?.ComponentPlayer,
                    active: false);
                break;
            default:
                break;
        }
    }

    public override bool ShouldSkipClientUpdate(IUpdateable updateable) {
        return NeorxnaCompatNetRuntime.ShouldSkipClientUpdate(updateable);
    }

    private static class NeorxnaCompatNetPatcher {
        private const string ComponentDashTypeName = "Neorxna.Components.ComponentDash";
        private const string LegacyComponentDashTypeName = "neorxna.ComponentDash";
        private const string ComponentPlayerTypeName = "Game.ComponentPlayer";
        private const string DashActiveFieldName = "_1680";

        public static bool Patch(ModAssemblyImage image) {
            using MemoryStream input = new(image.Bytes, writable: false);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                input,
                new ReaderParameters { InMemory = true, ReadSymbols = false });

            TypeDefinition componentDash = FindType(assembly, ComponentDashTypeName)
                ?? FindType(assembly, LegacyComponentDashTypeName);
            if (componentDash == null) {
                NeorxnaCompatNetRuntime.WarnMissingTarget(
                    $"Target type {ComponentDashTypeName} was not found.");
                return false;
            }

            MethodDefinition update = componentDash.Methods.FirstOrDefault(method =>
                !method.IsStatic
                && method.Name == "Update"
                && method.ReturnType.FullName == "System.Void"
                && method.Parameters.Count == 1
                && method.Parameters[0].ParameterType.FullName == "System.Single");
            if (update == null || !update.HasBody || update.Body.Instructions.Count == 0) {
                NeorxnaCompatNetRuntime.WarnMissingTarget(
                    $"Target method {ComponentDashTypeName}.Update(float) was not found.");
                return false;
            }

            FieldDefinition dashActiveField = componentDash.Fields.FirstOrDefault(field =>
                field.Name == DashActiveFieldName
                && field.FieldType.FullName == "System.Boolean");
            if (dashActiveField == null) {
                NeorxnaCompatNetRuntime.WarnMissingTarget(
                    $"Target dash state field {DashActiveFieldName} was not found.");
            }

            FieldDefinition componentPlayerField = componentDash.Fields.FirstOrDefault(field =>
                field.FieldType.FullName == ComponentPlayerTypeName);
            if (componentPlayerField == null) {
                NeorxnaCompatNetRuntime.WarnMissingTarget(
                    $"Target ComponentPlayer field on {componentDash.FullName} was not found.");
                return false;
            }

            MethodInfo bridgeMethod = typeof(NeorxnaCompatNetRuntime).GetMethod(
                nameof(NeorxnaCompatNetRuntime.ShouldSkipDashUpdate),
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo stateBridgeMethod = typeof(NeorxnaCompatNetRuntime).GetMethod(
                nameof(NeorxnaCompatNetRuntime.SetDashActiveBridge),
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo clearBridgeMethod = typeof(NeorxnaCompatNetRuntime).GetMethod(
                nameof(NeorxnaCompatNetRuntime.ClearDashActiveBridge),
                BindingFlags.Public | BindingFlags.Static);
            if (bridgeMethod == null) {
                return false;
            }

            ModuleDefinition module = assembly.MainModule;
            MethodReference bridge = module.ImportReference(bridgeMethod);
            MethodReference stateBridge = stateBridgeMethod == null ? null : module.ImportReference(stateBridgeMethod);
            MethodReference clearBridge = clearBridgeMethod == null ? null : module.ImportReference(clearBridgeMethod);
            if (update.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference reference
                    && reference.Name == bridge.Name
                    && reference.DeclaringType.FullName == bridge.DeclaringType.FullName)) {
                return false;
            }

            ILProcessor il = update.Body.GetILProcessor();
            Instruction originalStart = update.Body.Instructions[0];
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldfld, componentPlayerField));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse_S, originalStart));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));

            if (dashActiveField != null && stateBridge != null && clearBridge != null) {
                ReplaceDashStateWrites(
                    componentDash,
                    update,
                    dashActiveField,
                    stateBridge,
                    clearBridge);
            }

            using MemoryStream output = new();
            assembly.Write(output, new WriterParameters { WriteSymbols = false });
            image.Bytes = output.ToArray();
            return true;
        }

        private static bool ReplaceDashStateWrites(
            TypeDefinition componentDash,
            MethodDefinition update,
            FieldDefinition dashActiveField,
            MethodReference stateBridge,
            MethodReference clearBridge) {
            if (dashActiveField == null || stateBridge == null || clearBridge == null) {
                return false;
            }

            bool changed = false;
            foreach (MethodDefinition method in componentDash.Methods) {
                if (!method.HasBody) {
                    continue;
                }

                foreach (Instruction instruction in method.Body.Instructions) {
                    if (instruction.OpCode != OpCodes.Stfld
                        || instruction.Operand is not FieldReference reference
                        || reference.Name != dashActiveField.Name
                        || reference.DeclaringType.FullName != dashActiveField.DeclaringType.FullName) {
                        continue;
                    }

                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = method.Name == "StopDashing" ? clearBridge : stateBridge;
                    changed = true;
                }
            }

            return changed;
        }

        private static TypeDefinition FindType(AssemblyDefinition assembly, string fullName) {
            return assembly.MainModule.Types
                .SelectMany(FlattenTypes)
                .FirstOrDefault(type => type.FullName == fullName);
        }

        private static IEnumerable<TypeDefinition> FlattenTypes(TypeDefinition type) {
            yield return type;
            foreach (TypeDefinition nested in type.NestedTypes.SelectMany(FlattenTypes)) {
                yield return nested;
            }
        }
    }
}
