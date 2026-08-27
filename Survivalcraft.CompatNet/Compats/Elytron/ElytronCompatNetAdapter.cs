using System.Reflection;
using Engine;
using Game.Network;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Compatibility adapter for the original Elytron mod.
/// </summary>
public sealed class ElytronCompatNetAdapter : CompatNetAdapterLoader, ICompatNetAdapter {
    public const ushort StableAdapterId = 3;
    public const string TargetPackageName = "Elytron";

    public override ushort AdapterId => StableAdapterId;
    public override string AdapterName => TargetPackageName;

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        foreach (ModAssemblyImage image in assemblies) {
            if (image == null
                || image.Owner?.modInfo == null
                || image.Owner.modInfo.PackageName != TargetPackageName) {
                continue;
            }

            try {
                if (ElytronCompatNetPatcher.Patch(image)) {
                    Log.Information($"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                }
            }
            catch (Exception e) {
                // A third-party image must never prevent the remaining mods from loading.
                Log.Error($"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
            }
        }
    }

    public override void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (packet == null || packet.Payload == null) {
            return;
        }

        try {
            switch ((ElytronCompatNetOperation)packet.Operation) {
                case ElytronCompatNetOperation.JumpPressed: {
                    ElytronJumpPressedRequest request = new();
                    CompatNetPayload.Read(packet.Payload, request.Serialize);
                    ElytronCompatNetRuntime.TryExecuteServerJump(
                        playerData?.ComponentPlayer,
                        request.Sequence);
                    break;
                }
                case ElytronCompatNetOperation.FireworkIntent: {
                    ElytronFireworkIntentRequest request = new();
                    CompatNetPayload.Read(packet.Payload, request.Serialize);
                    ElytronCompatNetRuntime.TryExecuteServerFireworks(
                        playerData?.ComponentPlayer,
                        request.Sequence);
                    break;
                }
                default:
                    break;
            }
        }
        catch (Exception e) {
            Log.Warning($"[Survivalcraft.CompatNet] Ignored malformed Elytron request: {e.Message}");
        }
    }

    private static class ElytronCompatNetPatcher {
        private const string GlidingTypeName = "Elytron.ComponentGliding";
        private const string UpdateMethodName = "Update";
        private const string GlidingSetterName = "set_IsGlidingActive";
        private const string FireworksMethodName = "FireworksFly";

        public static bool Patch(ModAssemblyImage image) {
            using MemoryStream input = new(image.Bytes, writable: false);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                input,
                new ReaderParameters { InMemory = true, ReadSymbols = false });

            TypeDefinition componentGliding = FindType(assembly, GlidingTypeName);
            MethodDefinition update = FindUpdateMethod(componentGliding);
            if (update == null || !update.HasBody || update.Body.Instructions.Count == 0) {
                return false;
            }

            ModuleDefinition module = assembly.MainModule;
            MethodInfo skipMethod = typeof(ElytronCompatNetRuntime).GetMethod(
                nameof(ElytronCompatNetRuntime.ShouldSkipGlidingUpdate),
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo stateMethod = typeof(ElytronCompatNetRuntime).GetMethod(
                nameof(ElytronCompatNetRuntime.SetGlidingActiveBridge),
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo jumpMethod = typeof(ElytronCompatNetRuntime).GetMethod(
                nameof(ElytronCompatNetRuntime.CaptureJumpIntent),
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo fireworksMethod = typeof(ElytronCompatNetRuntime).GetMethod(
                nameof(ElytronCompatNetRuntime.FireworksFlyBridge),
                BindingFlags.Public | BindingFlags.Static);
            if (skipMethod == null) {
                return false;
            }

            MethodReference skipBridge = module.ImportReference(skipMethod);
            MethodReference stateBridge = stateMethod == null ? null : module.ImportReference(stateMethod);
            MethodReference jumpBridge = jumpMethod == null ? null : module.ImportReference(jumpMethod);
            MethodReference fireworksBridge = fireworksMethod == null ? null : module.ImportReference(fireworksMethod);
            PropertyDefinition stateProperty = componentGliding.Properties.FirstOrDefault(
                property => property.Name == "IsGlidingActive"
                    && property.SetMethod != null
                    && !property.SetMethod.IsStatic
                    && property.SetMethod.Parameters.Count == 1
                    && property.SetMethod.Parameters[0].ParameterType.FullName == "System.Boolean");
            MethodDefinition fireworks = componentGliding.Methods.FirstOrDefault(
                method => method.Name == FireworksMethodName
                    && !method.IsStatic
                    && method.ReturnType.FullName == "System.Void"
                    && method.Parameters.Count == 0);
            if (stateProperty?.SetMethod == null || fireworks == null) {
                ElytronCompatNetRuntime.WarnMissingTarget(
                    "Elytron action methods IsGlidingActive/FireworksFly were not found.");
            }

            bool changed = false;
            if (!HasCall(update, skipBridge)) {
                ILProcessor il = update.Body.GetILProcessor();
                Instruction originalStart = update.Body.Instructions[0];
                Instruction continueInstruction = Instruction.Create(OpCodes.Brfalse_S, originalStart);
                Instruction returnInstruction = Instruction.Create(OpCodes.Ret);

                il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, skipBridge));
                il.InsertBefore(originalStart, continueInstruction);
                il.InsertBefore(originalStart, returnInstruction);
                changed = true;
            }

            if (jumpBridge != null && !HasCall(update, jumpBridge)) {
                ILProcessor il = update.Body.GetILProcessor();
                Instruction originalStart = update.Body.Instructions[0];
                il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, jumpBridge));
                changed = true;
            }

            if (stateProperty?.SetMethod != null && stateBridge != null) {
                changed |= ReplaceCalls(update, stateProperty.SetMethod, stateBridge);
            }
            if (fireworks != null && fireworksBridge != null) {
                changed |= ReplaceCalls(update, fireworks, fireworksBridge);
            }

            if (!changed) {
                return false;
            }
            using MemoryStream output = new();
            assembly.Write(output, new WriterParameters { WriteSymbols = false });
            image.Bytes = output.ToArray();
            return true;
        }

        private static bool HasCall(MethodDefinition method, MethodReference bridge) {
            if (bridge == null) {
                return false;
            }

            return method.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference reference
                && reference.Name == bridge.Name
                && reference.DeclaringType.FullName == bridge.DeclaringType.FullName);
        }

        private static bool ReplaceCalls(
            MethodDefinition method,
            MethodDefinition target,
            MethodReference bridge) {
            if (target == null || bridge == null || !method.HasBody) {
                return false;
            }

            bool changed = false;
            foreach (Instruction instruction in method.Body.Instructions) {
                if (instruction.Operand is not MethodReference reference
                    || reference.Name != target.Name
                    || reference.DeclaringType.FullName != target.DeclaringType.FullName
                    || reference.Parameters.Count != target.Parameters.Count) {
                    continue;
                }

                instruction.OpCode = OpCodes.Call;
                instruction.Operand = bridge;
                changed = true;
            }

            return changed;
        }

        private static MethodDefinition FindUpdateMethod(TypeDefinition type) {
            if (type == null) {
                return null;
            }

            return FlattenTypes(type)
                .SelectMany(item => item.Methods)
                .FirstOrDefault(method =>
                    !method.IsStatic
                    && method.Name == UpdateMethodName
                    && method.ReturnType.FullName == "System.Void"
                    && method.Parameters.Count == 1
                    && method.Parameters[0].ParameterType.FullName == "System.Single");
        }

        private static TypeDefinition FindType(
            AssemblyDefinition assembly,
            string fullName) {
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
