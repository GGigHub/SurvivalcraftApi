using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Server-authoritative compatibility layer for the zh.command mod.
/// </summary>
public sealed class CommandCompatNetAdapter : ICompatNetAdapter {
    public const ushort StableAdapterId = 2;
    public const string TargetPackageName = "zh.command";

    public ushort AdapterId => StableAdapterId;
    public string AdapterName => TargetPackageName;

    public void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        foreach (ModAssemblyImage image in assemblies) {
            if (!string.Equals(
                    image.Owner.modInfo?.PackageName,
                    TargetPackageName,
                    StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            try {
                if (CommandCompatNetPatcher.Patch(image)) {
                    Log.Information($"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                }
            }
            catch (Exception e) {
                Log.Error($"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
            }
        }
    }

    public void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        switch ((CommandCompatNetOperation)packet.Operation) {
            case CommandCompatNetOperation.EditBlock: {
                CommandEditRequest request = new();
                CompatNetPayload.Read(packet.Payload, request.Serialize);
                if (CommandCompatNetRuntime.TryApplyCommandEdit(
                        playerData.ComponentPlayer,
                        request.Position,
                        request.Line,
                        out CommandBlockSync sync)) {
                    CompatNetCore.QueueBroadcast(
                        StableAdapterId,
                        (ushort)CommandCompatNetOperation.EditBlock,
                        CompatNetPayload.Write(sync.Serialize));
                }
                break;
            }
            case CommandCompatNetOperation.Execute: {
                CommandExecuteRequest request = new();
                CompatNetPayload.Read(packet.Payload, request.Serialize);
                CommandCompatNetRuntime.ExecuteCommandRequest(
                    playerData.ComponentPlayer,
                    request.Position,
                    request.Line,
                    request.Judge,
                    request.Source);
                break;
            }
            case CommandCompatNetOperation.RequestElectricSnapshot:
                CommandCompatNetRuntime.SendElectricSnapshot(packet.From);
                break;
        }
    }

    public void HandleBroadcast(CompatNetPacket packet) {
        switch ((CommandCompatNetOperation)packet.Operation) {
            case CommandCompatNetOperation.EditBlock: {
                CommandBlockSync sync = new();
                CompatNetPayload.Read(packet.Payload, sync.Serialize);
                CommandCompatNetRuntime.ApplyCommandBlockSync(sync);
                break;
            }
            case CommandCompatNetOperation.ElectricResult: {
                CommandElectricResult result = new();
                CompatNetPayload.Read(packet.Payload, result.Serialize);
                CommandCompatNetRuntime.ApplyElectricResult(result);
                break;
            }
            case CommandCompatNetOperation.ElectricSnapshot: {
                CommandElectricSnapshot snapshot = new();
                CompatNetPayload.Read(packet.Payload, snapshot.Serialize);
                foreach (CommandElectricResult result in snapshot.Results) {
                    CommandCompatNetRuntime.ApplyElectricResult(result);
                }
                break;
            }
        }
    }

    private static class CommandCompatNetPatcher {
        private const string CommandDataTypeName = "Command.CommandData";
        private const string SubmitDeclaringTypeName = "Command.SubsystemCommand";

        public static bool Patch(ModAssemblyImage image) {
            using MemoryStream input = new(image.Bytes, writable: false);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                input,
                new ReaderParameters { InMemory = true, ReadSymbols = false });

            ModuleDefinition module = assembly.MainModule;
            MethodReference manualBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.SubmitCommandBridge));
            MethodReference electricBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.SubmitElectricCommandBridge));
            MethodReference electricSimulationGuard = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.ShouldSkipElectricSimulation));
            MethodReference editBridge = ResolveEditBridge(module);
            bool changed = false;

            TypeDefinition electricElement = FindType(assembly, "Command.CommandElectricElement");
            changed |= ReplaceSubmitCalls(electricElement, electricBridge);
            changed |= InjectElectricSimulationGuard(electricElement, electricSimulationGuard);
            changed |= ReplaceSubmitCalls(
                FindType(assembly, "Command.SubsystemCmdRodBlockBehavior"),
                manualBridge);
            changed |= ReplaceSubmitCalls(
                FindType(assembly, "Command.ComponentHotKey"),
                manualBridge);
            changed |= ReplaceSubmitCalls(
                FindType(assembly, "Command.WithdrawBlockManager"),
                manualBridge);
            changed |= ReplaceSubmitCalls(
                FindType(assembly, "Game.CommandEditWidget"),
                manualBridge);
            changed |= InjectCommandEditNotification(
                FindType(assembly, "Game.CommandEditWidget"),
                editBridge);

            if (!changed) {
                return false;
            }

            using MemoryStream output = new();
            assembly.Write(output, new WriterParameters { WriteSymbols = false });
            image.Bytes = output.ToArray();
            return true;
        }

        private static MethodReference ResolveBridge(ModuleDefinition module, string methodName) {
            MethodInfo method = typeof(CommandCompatNetRuntime).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static);
            return module.ImportReference(method);
        }

        private static MethodReference ResolveEditBridge(ModuleDefinition module) {
            MethodInfo method = typeof(CommandCompatNetRuntime).GetMethod(
                nameof(CommandCompatNetRuntime.NotifyCommandBlockEdited),
                BindingFlags.Public | BindingFlags.Static);
            return module.ImportReference(method);
        }

        private static bool ReplaceSubmitCalls(TypeDefinition type, MethodReference bridge) {
            if (type == null) {
                return false;
            }

            bool changed = false;
            foreach (MethodDefinition method in FlattenTypes(type).SelectMany(item => item.Methods)) {
                if (!method.HasBody) {
                    continue;
                }

                ILProcessor il = method.Body.GetILProcessor();
                for (int i = 0; i < method.Body.Instructions.Count; i++) {
                    Instruction instruction = method.Body.Instructions[i];
                    if (instruction.Operand is not MethodReference reference
                        || reference.Name != "Submit"
                        || reference.DeclaringType.FullName != SubmitDeclaringTypeName
                        || reference.Parameters.Count != 3
                        || reference.Parameters[0].ParameterType.FullName != "System.String"
                        || reference.Parameters[1].ParameterType.FullName != CommandDataTypeName
                        || reference.Parameters[2].ParameterType.FullName != "System.Boolean") {
                        continue;
                    }

                    TypeReference returnType = moduleImport(type, reference.ReturnType);
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = bridge;
                    Instruction convert = returnType.IsValueType
                        ? Instruction.Create(OpCodes.Unbox_Any, returnType)
                        : Instruction.Create(OpCodes.Castclass, returnType);
                    il.InsertAfter(instruction, convert);
                    i++;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool InjectElectricSimulationGuard(
            TypeDefinition type,
            MethodReference guard) {
            if (type == null) {
                return false;
            }

            MethodDefinition method = FlattenTypes(type)
                .SelectMany(item => item.Methods)
                .FirstOrDefault(item =>
                    item.Name == "Simulate"
                    && item.Parameters.Count == 0
                    && item.ReturnType.FullName == "System.Boolean");
            if (method == null || !method.HasBody) {
                return false;
            }

            if (method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference reference
                    && reference.Name == nameof(CommandCompatNetRuntime.ShouldSkipElectricSimulation))) {
                return false;
            }

            ILProcessor il = method.Body.GetILProcessor();
            Instruction first = method.Body.Instructions[0];
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, Instruction.Create(OpCodes.Call, guard));
            il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_0));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
            return true;
        }

        private static bool InjectCommandEditNotification(
            TypeDefinition type,
            MethodReference bridge) {
            if (type == null) {
                return false;
            }

            MethodDefinition method = type.Methods.FirstOrDefault(item =>
                item.Name == "SaveCommandData"
                && item.Parameters.Count == 1
                && item.Parameters[0].ParameterType.FullName == "System.Boolean");
            if (method == null || !method.HasBody) {
                return false;
            }

            FieldReference playerField = type.Fields.FirstOrDefault(field =>
                field.Name == "m_componentPlayer"
                && field.FieldType.FullName == "Game.ComponentPlayer");
            FieldReference positionField = type.Fields.FirstOrDefault(field =>
                field.Name == "m_position"
                && field.FieldType.FullName == "Engine.Point3");
            if (playerField == null || positionField == null) {
                return false;
            }

            ILProcessor il = method.Body.GetILProcessor();
            for (int i = 0; i < method.Body.Instructions.Count - 1; i++) {
                Instruction call = method.Body.Instructions[i];
                if (call.Operand is not MethodReference reference
                    || reference.Name != "SetCommandData"
                    || reference.DeclaringType.FullName != "Command.SubsystemCommandBlockBehavior") {
                    continue;
                }

                Instruction store = method.Body.Instructions[i + 1];
                if (store.OpCode.Code is not Code.Stloc
                    and not Code.Stloc_0
                    and not Code.Stloc_1
                    and not Code.Stloc_2
                    and not Code.Stloc_3) {
                    continue;
                }

                VariableDefinition commandLinesLocal = FindCommandLinesLocal(method);
                if (commandLinesLocal == null) {
                    return false;
                }
                Instruction[] prefix = [
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldfld, playerField),
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldfld, positionField),
                    Instruction.Create(OpCodes.Ldloc, commandLinesLocal),
                    Instruction.Create(OpCodes.Call, bridge)
                ];

                foreach (Instruction instruction in prefix) {
                    il.InsertAfter(store, instruction);
                    store = instruction;
                }
                return true;
            }

            return false;
        }

        private static VariableDefinition FindCommandLinesLocal(MethodDefinition method) {
            for (int i = 0; i < method.Body.Instructions.Count - 1; i++) {
                Instruction instruction = method.Body.Instructions[i];
                if (instruction.Operand is not MethodReference reference
                    || reference.Name != "GetCommandLines"
                    || reference.ReturnType.FullName != "System.String") {
                    continue;
                }
                Instruction store = method.Body.Instructions[i + 1];
                if (store.Operand is VariableDefinition variable
                    && variable.VariableType.FullName == "System.String") {
                    return variable;
                }
            }

            return method.Body.Variables.FirstOrDefault(variable =>
                variable.VariableType.FullName == "System.String");
        }

        private static TypeReference moduleImport(TypeDefinition type, TypeReference reference) {
            return type.Module.ImportReference(reference);
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
