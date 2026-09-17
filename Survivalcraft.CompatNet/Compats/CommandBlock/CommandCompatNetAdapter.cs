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

    public bool ShouldSkipClientUpdate(IUpdateable updateable) {
        return CommandCompatNetRuntime.ShouldSkipClientUpdate(updateable);
    }

    public void OnServerUpdate(float dt) {
        CommandCompatNetRuntime.OnServerUpdate(dt);
    }

    public bool ShouldSkipServerUpdate(IUpdateable updateable) {
        return CommandCompatNetRuntime.ShouldSkipServerUpdate(updateable);
    }

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
            case CommandCompatNetOperation.ExecuteCommand: {
                CommandExecuteRequest request = new();
                CompatNetPayload.Read(packet.Payload, request.Serialize);
                CommandTerrainSnapshot before = CommandCompatNetRuntime.CaptureTerrainSnapshot(request.Position);
                bool executed = CommandCompatNetRuntime.TryExecuteCommand(
                    playerData,
                    request,
                    out int resultCode);
                if (executed) {
                    CommandCompatNetRuntime.SendTerrainSnapshot(before, packet.From);
                    CommandCompatNetRuntime.SendMovingBlockSnapshot(packet.From);
                    CommandCompatNetRuntime.SendCreatureSnapshot(packet.From);
                    CommandCompatNetRuntime.SendWorldStateSnapshot(packet.From);
                }
                if (packet.From != null) {
                    CommandExecuteResult result = new() {
                        RequestId = request.RequestId,
                        ResultCode = resultCode
                    };
                    CompatNetCore.QueueBroadcast(
                        StableAdapterId,
                        (ushort)CommandCompatNetOperation.ExecuteResult,
                        CompatNetPayload.Write(result.Serialize),
                        to: packet.From);
                    CommandCompatNetRuntime.SendMovingBlockSnapshot(packet.From);
                    CommandCompatNetRuntime.SendCreatureSnapshot(packet.From);
                    // Queue one final authoritative player snapshot after the
                    // result marker. Legacy health/vital-stat packets can be
                    // delivered independently and otherwise overwrite a
                    // just-applied command result for one frame.
                    CommandCompatNetRuntime.SendPlayerStateSnapshot();
                }
                break;
            }
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
            case CommandCompatNetOperation.RequestElectricSnapshot:
                CommandCompatNetRuntime.SendElectricSnapshot(packet.From);
                break;
        }
    }

    public void HandleBroadcast(CompatNetPacket packet) {
        switch ((CommandCompatNetOperation)packet.Operation) {
            case CommandCompatNetOperation.ExecuteResult: {
                CommandExecuteResult result = new();
                CompatNetPayload.Read(packet.Payload, result.Serialize);
                CommandCompatNetRuntime.ApplyCommandResult(result);
                break;
            }
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
            case CommandCompatNetOperation.PlayerState: {
                CommandPlayerStateSync state = new();
                CompatNetPayload.Read(packet.Payload, state.Serialize);
                CommandCompatNetRuntime.ApplyPlayerState(state);
                break;
            }
            case CommandCompatNetOperation.TerrainSnapshot: {
                CommandTerrainSnapshot snapshot = new();
                CompatNetPayload.Read(packet.Payload, snapshot.Serialize);
                CommandCompatNetRuntime.ApplyTerrainSnapshot(snapshot);
                break;
            }
            case CommandCompatNetOperation.MovingBlockSnapshot: {
                CommandMovingBlockSnapshot snapshot = new();
                CompatNetPayload.Read(packet.Payload, snapshot.Serialize);
                CommandCompatNetRuntime.ApplyMovingBlockSnapshot(snapshot);
                break;
            }
            case CommandCompatNetOperation.CreatureSnapshot: {
                CommandCreatureSnapshot snapshot = new();
                CompatNetPayload.Read(packet.Payload, snapshot.Serialize);
                CommandCompatNetRuntime.ApplyCreatureSnapshot(snapshot);
                break;
            }
            case CommandCompatNetOperation.WorldState: {
                CommandWorldStateSync state = new();
                CompatNetPayload.Read(packet.Payload, state.Serialize);
                CommandCompatNetRuntime.ApplyWorldState(state);
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
            MethodReference electricBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.SubmitElectricCommandBridge));
            MethodReference commandBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.SubmitCommandBridge));
            MethodReference commandMethodBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.SubmitCommandMethodBridge));
            MethodReference terrainBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.SetCommandTerrainCell));
            MethodReference chunkUpdateBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.UpdateCommandChunks));
            MethodReference commandTaskBridge = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.RunCommandTask));
            MethodReference electricSimulationGuard = ResolveBridge(
                module,
                nameof(CommandCompatNetRuntime.ShouldSkipElectricSimulation));
            MethodReference editBridge = ResolveEditBridge(module);
            bool changed = false;

            TypeDefinition electricElement = FindType(assembly, "Command.CommandElectricElement");
            changed |= ReplaceSubmitImplementation(
                FindType(assembly, "Command.SubsystemCommand"),
                commandMethodBridge);
            changed |= ReplaceSubmitCalls(electricElement, electricBridge);
            foreach (TypeDefinition type in assembly.MainModule.Types.SelectMany(FlattenTypes).ToArray()) {
                changed |= ReplaceSubmitCalls(type, commandBridge);
            }
            changed |= ReplaceCommandTerrainWrites(
                FindType(assembly, "Command.SubsystemCommandDef"),
                terrainBridge);
            changed |= ReplaceCommandChunkUpdates(
                FindType(assembly, "Command.SubsystemCommandDef"),
                chunkUpdateBridge);
            changed |= ReplaceCommandTaskRuns(assembly, commandTaskBridge);
            changed |= InjectElectricSimulationGuard(electricElement, electricSimulationGuard);
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

        private static bool ReplaceSubmitImplementation(
            TypeDefinition type,
            MethodReference bridge) {
            if (type == null) {
                return false;
            }

            MethodDefinition original = type.Methods.FirstOrDefault(method =>
                method.Name == "Submit"
                && method.ReturnType.FullName == "Command.SubmitResult"
                && method.Parameters.Count == 3
                && method.Parameters[0].ParameterType.FullName == "System.String"
                && method.Parameters[1].ParameterType.FullName == "Command.CommandData"
                && method.Parameters[2].ParameterType.FullName == "System.Boolean");
            if (original == null
                || type.Methods.Any(method => method.Name == "SubmitCompatNetOriginal")) {
                return false;
            }

            original.Name = "SubmitCompatNetOriginal";
            MethodDefinition wrapper = new(
                "Submit",
                original.Attributes,
                type.Module.ImportReference(original.ReturnType));
            foreach (ParameterDefinition parameter in original.Parameters) {
                wrapper.Parameters.Add(new ParameterDefinition(
                    parameter.Name,
                    parameter.Attributes,
                    type.Module.ImportReference(parameter.ParameterType)));
            }

            ILProcessor il = wrapper.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            for (int i = 0; i < wrapper.Parameters.Count; i++) {
                il.Append(Instruction.Create(OpCodes.Ldarg, wrapper.Parameters[i]));
            }
            il.Append(Instruction.Create(OpCodes.Call, bridge));
            il.Append(wrapper.ReturnType.IsValueType
                ? Instruction.Create(OpCodes.Unbox_Any, wrapper.ReturnType)
                : Instruction.Create(OpCodes.Castclass, wrapper.ReturnType));
            il.Append(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(wrapper);
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
                if (!method.HasBody || method.Name == "SubmitCompatNetOriginal") {
                    continue;
                }

                ILProcessor il = method.Body.GetILProcessor();
                for (int i = 0; i < method.Body.Instructions.Count; i++) {
                    Instruction instruction = method.Body.Instructions[i];
                    if (instruction.Operand is not MethodReference reference
                        || reference.Name != "Submit"
                        || !IsSubmitDeclaringType(reference.DeclaringType.FullName)
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

        private static bool IsSubmitDeclaringType(string fullName) {
            return string.Equals(fullName, SubmitDeclaringTypeName, StringComparison.Ordinal)
                || fullName?.EndsWith(".SubsystemCommandDef", StringComparison.Ordinal) == true
                || fullName?.EndsWith("+SubsystemCommandDef", StringComparison.Ordinal) == true;
        }

        private static bool ReplaceCommandTerrainWrites(
            TypeDefinition type,
            MethodReference bridge) {
            if (type == null) {
                return false;
            }

            bool changed = false;
            foreach (MethodDefinition method in FlattenTypes(type).SelectMany(item => item.Methods)) {
                if (!method.HasBody || method.Name != "ChangeBlockValue") {
                    continue;
                }

                foreach (Instruction instruction in method.Body.Instructions) {
                    if (instruction.Operand is not MethodReference reference
                        || reference.Name != "SetCellValueFast"
                        || reference.Parameters.Count != 4
                        || reference.ReturnType.FullName != "System.Void") {
                        continue;
                    }
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = bridge;
                    changed = true;
                }
            }
            return changed;
        }

        private static bool ReplaceCommandChunkUpdates(
            TypeDefinition type,
            MethodReference bridge) {
            if (type == null) {
                return false;
            }

            MethodDefinition method = FlattenTypes(type)
                .SelectMany(item => item.Methods)
                .FirstOrDefault(item =>
                    item.Name == "UpdateChunks"
                    && item.ReturnType.FullName == "System.Void"
                    && item.Parameters.Count == 2
                    && item.Parameters.All(parameter =>
                        parameter.ParameterType.FullName == "Engine.Point3"));
            if (method == null || !method.HasBody) {
                return false;
            }

            if (method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference reference
                    && reference.Name == nameof(CommandCompatNetRuntime.UpdateCommandChunks))) {
                return false;
            }

            method.Body.Instructions.Clear();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            ILProcessor il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldarg_1));
            il.Append(Instruction.Create(OpCodes.Ldarg_2));
            il.Append(Instruction.Create(OpCodes.Call, bridge));
            il.Append(Instruction.Create(OpCodes.Ret));
            return true;
        }

        private static bool ReplaceCommandTaskRuns(
            AssemblyDefinition assembly,
            MethodReference bridge) {
            bool changed = false;
            foreach (TypeDefinition type in assembly.MainModule.Types.SelectMany(FlattenTypes).ToArray()) {
                foreach (MethodDefinition method in type.Methods) {
                    if (!method.HasBody) {
                        continue;
                    }
                    foreach (Instruction instruction in method.Body.Instructions) {
                        if (instruction.Operand is not MethodReference reference
                            || reference.Name != "Run"
                            || reference.DeclaringType.FullName != "System.Threading.Tasks.Task"
                            || reference.Parameters.Count != 1
                            || reference.Parameters[0].ParameterType.FullName != "System.Action"
                            || reference.ReturnType.FullName != "System.Threading.Tasks.Task") {
                            continue;
                        }
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = bridge;
                        changed = true;
                    }
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
