using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Server-authoritative compatibility for the input-driven parts of MCHszr.
/// The adapter references only the common game model. MCHszr symbols are
/// discovered by Cecil metadata and reflection after the optional mod loads.
/// </summary>
public sealed class MCHszrCompatNetAdapter : CompatNetAdapterLoader, ICompatNetAdapter {
    public const ushort StableAdapterId = 6;
    public const string TargetPackageName = "MCHszr";
    public const string TargetAssemblyFileName = "MCHszr.dll";

    public override ushort AdapterId => StableAdapterId;
    public override string AdapterName => TargetPackageName;

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        foreach (ModAssemblyImage image in assemblies ?? Array.Empty<ModAssemblyImage>()) {
            if (image == null) {
                continue;
            }

            bool isTargetPackage = string.Equals(
                image.Owner?.modInfo?.PackageName,
                TargetPackageName,
                StringComparison.OrdinalIgnoreCase);
            bool isTargetAssembly = string.Equals(
                Path.GetFileName(image.FileName),
                TargetAssemblyFileName,
                StringComparison.OrdinalIgnoreCase);
            if (!isTargetPackage && !isTargetAssembly) {
                continue;
            }

            try {
                if (MCHszrCompatNetPatcher.Patch(image)) {
                    Log.Information(
                        $"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                }
            }
            catch (Exception e) {
                // Optional third-party IL must never prevent the remaining
                // mod set from loading.
                Log.Error(
                    $"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
            }
        }
    }

    public override void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (packet == null || playerData?.ComponentPlayer == null) {
            return;
        }
        MCHszrCompatNetRequest request = new();
        CompatNetPayload.Read(packet.Payload, request.Serialize);

        switch ((MCHszrCompatNetOperation)packet.Operation) {
            case MCHszrCompatNetOperation.PoseToggle:
                MCHszrCompatNetRuntime.TryTogglePose(playerData, request);
                break;
            case MCHszrCompatNetOperation.ChargeBegin:
                MCHszrCompatNetRuntime.TryBeginCharge(playerData, request);
                break;
            case MCHszrCompatNetOperation.ChargeRelease:
                MCHszrCompatNetRuntime.TryReleaseCharge(playerData, request);
                break;
            case MCHszrCompatNetOperation.FoodConsume:
                MCHszrCompatNetRuntime.TryConsumeFood(playerData, request);
                break;
            case MCHszrCompatNetOperation.StateSnapshot:
                break;
            default:
                // Unknown operations are intentionally ignored for forward
                // compatibility and malformed-client containment.
                break;
        }
    }

    public override void HandleBroadcast(CompatNetPacket packet) {
        if (packet?.Operation == (ushort)MCHszrCompatNetOperation.StateSnapshot) {
            MCHszrCompatNetRuntime.ApplyStateSnapshot(packet.Payload);
        }
    }

    public override bool ShouldSkipClientUpdate(IUpdateable updateable) {
        return MCHszrCompatNetRuntime.ShouldSkipClientUpdate(updateable);
    }

    public override void OnServerUpdate(float dt) {
        MCHszrCompatNetRuntime.OnServerUpdate(dt);
    }

    private static class MCHszrCompatNetPatcher {
        private const string LoaderTypeName = "MCHszr.UnifiedMCHszrModLoader";
        private const string PoseBehaviorTypeName = "MCHszr.SubsystemSpearEditBehavior";
        private const string FoodBehaviorTypeName = "MCHszr.SubsystemMCEdibleItemBehavior";
        private const string PlayerTypeName = "Game.ComponentPlayer";
        private const string MinerTypeName = "Game.ComponentMiner";
        private const string InventoryTypeName = "Game.IInventory";

        public static bool Patch(ModAssemblyImage image) {
            using MemoryStream input = new(image.Bytes, writable: false);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                input,
                new ReaderParameters { InMemory = true, ReadSymbols = false });

            ModuleDefinition module = assembly.MainModule;
            MethodReference inputBridge = ResolveBridge(
                module,
                nameof(MCHszrCompatNetRuntime.TryHandlePlayerInput));
            MethodReference poseBridge = ResolveBridge(
                module,
                nameof(MCHszrCompatNetRuntime.TryQueuePoseToggle));
            MethodReference foodBridge = ResolveBridge(
                module,
                nameof(MCHszrCompatNetRuntime.TryHandleFoodConsume));

            bool inputChanged = PatchPlayerInput(FindType(assembly, LoaderTypeName), inputBridge);
            bool poseChanged = PatchPoseToggle(
                FindType(assembly, PoseBehaviorTypeName),
                poseBridge);
            bool foodChanged = PatchFoodConsume(
                FindType(assembly, FoodBehaviorTypeName),
                foodBridge);
            bool changed = inputChanged || poseChanged || foodChanged;

            if (!changed) {
                return false;
            }

            using MemoryStream output = new();
            assembly.Write(output, new WriterParameters { WriteSymbols = false });
            image.Bytes = output.ToArray();
            return true;
        }

        private static MethodReference ResolveBridge(
            ModuleDefinition module,
            string methodName) {
            MethodInfo method = typeof(MCHszrCompatNetRuntime).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null) {
                throw new MissingMethodException(
                    typeof(MCHszrCompatNetRuntime).FullName,
                    methodName);
            }

            return module.ImportReference(method);
        }

        private static bool PatchPlayerInput(
            TypeDefinition type,
            MethodReference bridge) {
            MethodDefinition method = type?.Methods.FirstOrDefault(candidate =>
                candidate.Name == "UpdatePlayerInputAim"
                && candidate.ReturnType.FullName == "System.Void"
                && candidate.Parameters.Count == 6
                && candidate.Parameters[0].ParameterType.FullName == PlayerTypeName
                && candidate.Parameters[1].ParameterType.FullName == "System.Boolean"
                && candidate.Parameters[2].ParameterType.FullName == "System.Boolean&"
                && candidate.Parameters[3].ParameterType.FullName == "System.Single&"
                && candidate.Parameters[4].ParameterType.FullName == "System.Boolean"
                && candidate.Parameters[5].ParameterType.FullName == "System.Boolean&");
            if (method == null || !method.HasBody || method.Body.Instructions.Count == 0) {
                return false;
            }

            if (ContainsBridgeCall(method, bridge)) {
                return false;
            }

            ILProcessor il = method.Body.GetILProcessor();
            Instruction originalStart = method.Body.Instructions[0];
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_1));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_2));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_3));
            il.InsertBefore(
                originalStart,
                Instruction.Create(OpCodes.Ldarg, method.Parameters[5]));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
            il.InsertBefore(
                originalStart,
                Instruction.Create(OpCodes.Brfalse, originalStart));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
            return true;
        }

        private static bool PatchPoseToggle(
            TypeDefinition type,
            MethodReference bridge) {
            MethodDefinition method = type?.Methods.FirstOrDefault(candidate =>
                candidate.Name == "OnEditInventoryItem"
                && candidate.ReturnType.FullName == "System.Boolean"
                && candidate.Parameters.Count == 3);
            if (method == null || !method.HasBody || method.Body.Instructions.Count == 0) {
                return false;
            }

            if (ContainsBridgeCall(method, bridge)) {
                return false;
            }

            // The bridge returns false so the original local toggle still
            // runs. The server receives the same intent and toggles its own
            // MCHszr state after validating the authenticated inventory slot.
            ILProcessor il = method.Body.GetILProcessor();
            Instruction originalStart = method.Body.Instructions[0];
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_1));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_2));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_3));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Pop));
            return true;
        }

        private static bool PatchFoodConsume(
            TypeDefinition type,
            MethodReference bridge) {
            MethodDefinition method = type?.Methods.FirstOrDefault(candidate =>
                candidate.Name == "ConsumeItemAndEat"
                && candidate.ReturnType.FullName == "System.Void"
                && candidate.Parameters.Count == 2
                && candidate.Parameters[0].ParameterType.FullName == MinerTypeName
                && candidate.Parameters[1].ParameterType.FullName == "System.Int32");
            if (method == null || !method.HasBody || method.Body.Instructions.Count == 0) {
                return false;
            }

            if (ContainsBridgeCall(method, bridge)) {
                return false;
            }

            ILProcessor il = method.Body.GetILProcessor();
            Instruction originalStart = method.Body.Instructions[0];
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_1));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_2));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
            il.InsertBefore(
                originalStart,
                Instruction.Create(OpCodes.Brfalse, originalStart));
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
            return true;
        }

        private static bool ContainsBridgeCall(
            MethodDefinition method,
            MethodReference bridge) {
            return method.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference reference
                && reference.Name == bridge.Name
                && reference.DeclaringType.FullName == bridge.DeclaringType.FullName);
        }

        private static TypeDefinition FindType(
            AssemblyDefinition assembly,
            string fullName) {
            TypeDefinition type = assembly.MainModule.Types
                .SelectMany(FlattenTypes)
                .FirstOrDefault(type => type.FullName == fullName);
            if (type != null) {
                return type;
            }

            int separator = fullName.LastIndexOf('.');
            string shortName = separator >= 0 ? fullName[(separator + 1)..] : fullName;
            return assembly.MainModule.Types
                .SelectMany(FlattenTypes)
                .FirstOrDefault(candidate => candidate.Name == shortName);
        }

        private static IEnumerable<TypeDefinition> FlattenTypes(TypeDefinition type) {
            yield return type;
            foreach (TypeDefinition nested in type.NestedTypes.SelectMany(FlattenTypes)) {
                yield return nested;
            }
        }
    }
}
