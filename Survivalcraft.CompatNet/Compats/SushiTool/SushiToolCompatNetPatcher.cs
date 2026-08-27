using System.Reflection;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Game;

/// <summary>
/// Adds the missing network seam to SushiTool's custom inventory component.
/// The patch is applied to the in-memory image only and is idempotent.
/// </summary>
public static class SushiToolCompatNetPatcher {
    public static bool PatchNetworkMod(ModAssemblyImage image) {
        if (image?.Bytes == null || image.Bytes.Length == 0) {
            return false;
        }

        using MemoryStream input = new(image.Bytes, writable: false);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            input,
            new ReaderParameters { InMemory = true, ReadSymbols = false });
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition networkLoader = FindType(
            assembly,
            "Survivalcraft.Multiplayer.NetworkModLoader");
        if (networkLoader == null) {
            return false;
        }

        MethodReference moveBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.TryQueueMoveItem));
        MethodReference dragDropBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.TryQueueDragDropItem));
        MethodReference playerDropBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.TryQueuePlayerDrop));
        bool changed = false;
        changed |= InjectBooleanHook(
            networkLoader,
            "OnNetworkMoveItem",
            moveBridge,
            [
                "Game.IInventory",
                "System.Int32",
                "Game.IInventory",
                "System.Int32",
                "System.Int32"
            ]);
        changed |= InjectBooleanHook(
            networkLoader,
            "OnNetworkDragDropItem",
            dragDropBridge,
            [
                "Game.IInventory",
                "System.Int32",
                "Game.DragMode",
                "Game.IInventory",
                "System.Int32",
                "System.Boolean",
                "System.Int32"
            ]);
        changed |= InjectBooleanHook(
            networkLoader,
            "OnNetworkPlayerDragDrop",
            playerDropBridge,
            [
                "Game.PlayerData",
                "System.Int32",
                "System.Int32",
                "Engine.Vector3",
                "System.Int32"
            ]);
        if (!changed) {
            return false;
        }

        using MemoryStream output = new();
        assembly.Write(output, new WriterParameters { WriteSymbols = false });
        image.Bytes = output.ToArray();
        return true;
    }

    public static bool Patch(ModAssemblyImage image) {
        if (image?.Bytes == null || image.Bytes.Length == 0) {
            return false;
        }

        using MemoryStream input = new(image.Bytes, writable: false);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            input,
            new ReaderParameters { InMemory = true, ReadSymbols = false });

        ModuleDefinition module = assembly.MainModule;
        MethodReference setSlotValueBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.SetSlotValueBridge));
        MethodReference notifyMutationBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifyInventoryMutation));
        MethodReference sharedSetSlotValueBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.SetSharedSlotValueBridge));
        MethodReference sharedNotifyMutationBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifySharedInventoryMutation));
        MethodReference addChannelBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.QueueAddChannelRequest));
        MethodReference updateChannelNameBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.TryQueueUpdateChannelName));
        MethodReference reorderChannelBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.TryQueueReorderChannel));
        MethodReference machineActionBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifyMachineWidgetAction));
        bool changed = false;

        TypeDefinition syncBox = FindType(assembly, SushiToolCompatNetRuntime.SyncBoxTypeName);
        changed |= EnsureSetSlotValue(syncBox, module, setSlotValueBridge);
        changed |= InjectMutationNotification(syncBox, notifyMutationBridge, "AddSlotItems");
        changed |= InjectMutationNotification(syncBox, notifyMutationBridge, "RemoveSlotItems");
        changed |= InjectMutationNotification(syncBox, notifyMutationBridge, "ProcessSlotItems");
        TypeDefinition syncInventory = FindType(
            assembly,
            SushiToolCompatNetRuntime.SyncInventoryTypeName);
        changed |= EnsureSetSlotValue(syncInventory, module, sharedSetSlotValueBridge);
        changed |= InjectMutationNotification(
            syncInventory,
            sharedNotifyMutationBridge,
            "AddSlotItems");
        changed |= InjectMutationNotification(
            syncInventory,
            sharedNotifyMutationBridge,
            "RemoveSlotItems");
        changed |= InjectMutationNotification(
            syncInventory,
            sharedNotifyMutationBridge,
            "ProcessSlotItems");
        changed |= InjectAddChannelGuard(
            FindType(assembly, SushiToolCompatNetRuntime.SyncBoxSubsystemTypeName),
            addChannelBridge);
        TypeDefinition syncBoxSubsystem = FindType(
            assembly,
            SushiToolCompatNetRuntime.SyncBoxSubsystemTypeName);
        changed |= InjectVoidBooleanHook(
            syncBoxSubsystem,
            "UpdateChannelName",
            updateChannelNameBridge,
            ["System.Int32", "System.String", "Engine.Color"]);
        changed |= InjectTrueReturningHook(
            syncBoxSubsystem,
            "ReorderChannelOrder",
            reorderChannelBridge,
            ["System.Int32", "System.Int32"]);
        changed |= InjectMachineWidgetHook(
            FindType(assembly, "Sushi.SushiMachineWidget`1"),
            machineActionBridge);
        MethodReference machineConfigBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifyMachineDialogClose));
        changed |= InjectMachineConfigHook(
            FindType(assembly, "Sushi.SushiMachineDialog`1"),
            machineConfigBridge);
        MethodReference farmerMachineConfigBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifyFarmerMachineDialogApply));
        changed |= InjectObjectHook(
            FindType(assembly, "Sushi.SushiFarmerMachineSettingDialog"),
            "ApplySettings",
            farmerMachineConfigBridge);
        MethodReference trainUpdateBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifyTrainUpdate));
        changed |= InjectTrainUpdateHook(
            FindType(assembly, "Sushi.ComponentSushiTrain"),
            trainUpdateBridge);
        MethodReference trainRecycleBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifyTrainRecycle));
        changed |= InjectBooleanObjectGuard(
            FindType(assembly, "Sushi.ComponentSushiTrain"),
            "TrainRecycle",
            trainRecycleBridge);
        MethodReference trainCallBridge = ResolveBridge(
            module,
            nameof(SushiToolCompatNetRuntime.NotifyTrainCall));
        changed |= InjectBooleanObjectGuard(
            FindType(assembly, "Sushi.ComponentSushiTrain"),
            "TrainCall",
            trainCallBridge);

        if (!changed) {
            return false;
        }

        using MemoryStream output = new();
        assembly.Write(output, new WriterParameters { WriteSymbols = false });
        image.Bytes = output.ToArray();
        return true;
    }

    private static bool InjectMachineWidgetHook(
        TypeDefinition type,
        MethodReference bridge) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == "Update"
            && item.ReturnType.FullName == "System.Void"
            && item.Parameters.Count == 0
            && item.HasBody);
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, bridge));
        return true;
    }

    private static bool InjectMachineConfigHook(
        TypeDefinition type,
        MethodReference bridge) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == "CloseDialog"
            && item.ReturnType.FullName == "System.Void"
            && item.Parameters.Count == 2
            && item.Parameters[0].ParameterType.FullName == "System.Boolean"
            && item.Parameters[1].ParameterType.FullName == "System.Boolean"
            && item.HasBody);
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_2));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, bridge));
        return true;
    }

    private static bool InjectObjectHook(
        TypeDefinition type,
        string methodName,
        MethodReference bridge) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == methodName
            && item.ReturnType.FullName == "System.Void"
            && item.Parameters.Count == 0
            && item.HasBody);
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, bridge));
        return true;
    }

    private static bool InjectTrainUpdateHook(
        TypeDefinition type,
        MethodReference bridge) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == "Update"
            && item.ReturnType.FullName == "System.Void"
            && item.Parameters.Count == 1
            && item.Parameters[0].ParameterType.FullName == "System.Single"
            && item.HasBody);
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, bridge));
        return true;
    }

    private static bool InjectBooleanObjectGuard(
        TypeDefinition type,
        string methodName,
        MethodReference bridge) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == methodName
            && item.ReturnType.FullName == "System.Void"
            && item.Parameters.Count == 0
            && item.HasBody);
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction originalStart = method.Body.Instructions[0];
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
        return true;
    }

    private static bool EnsureSetSlotValue(
        TypeDefinition type,
        ModuleDefinition module,
        MethodReference bridge) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == "SetSlotValue"
            && item.ReturnType.FullName == "System.Void"
            && item.Parameters.Count == 2
            && item.Parameters[0].ParameterType.FullName == "System.Int32"
            && item.Parameters[1].ParameterType.FullName == "System.Object");
        if (method != null) {
            // Do not replace a future SushiTool implementation. The standard
            // component is currently missing this override, which is why the
            // compatibility layer adds it only when absent.
            return false;
        }

        method = new MethodDefinition(
            "SetSlotValue",
            CecilMethodAttributes.Public
            | CecilMethodAttributes.Virtual
            | CecilMethodAttributes.HideBySig
            | CecilMethodAttributes.ReuseSlot,
            module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition(
            "slotIndex",
            CecilParameterAttributes.None,
            module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition(
            "obj",
            CecilParameterAttributes.None,
            module.TypeSystem.Object));
        method.IsNewSlot = false;
        MethodInfo baseMethod = typeof(ComponentInventoryBase).GetMethod(
            nameof(ComponentInventoryBase.SetSlotValue),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(int), typeof(object)],
            modifiers: null);
        if (baseMethod != null) {
            method.Overrides.Add(module.ImportReference(baseMethod));
        }
        MethodInfo interfaceMethod = typeof(IInventory).GetMethod(
            nameof(IInventory.SetSlotValue),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(int), typeof(object)],
            modifiers: null);
        if (interfaceMethod != null) {
            method.Overrides.Add(module.ImportReference(interfaceMethod));
        }
        type.Methods.Add(method);

        ILProcessor il = method.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Ldarg_2));
        il.Append(Instruction.Create(OpCodes.Call, bridge));
        il.Append(Instruction.Create(OpCodes.Ret));
        return true;
    }

    private static bool InjectMutationNotification(
        TypeDefinition type,
        MethodReference bridge,
        string methodName) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == methodName
            && item.HasBody
            && item.Parameters.Count >= 2
            && item.Parameters[0].ParameterType.FullName == "System.Int32");
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, bridge));
        return true;
    }

    private static bool InjectAddChannelGuard(
        TypeDefinition type,
        MethodReference bridge) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == "AddChannel"
            && item.ReturnType.FullName == "System.Int32"
            && item.Parameters.Count == 1
            && item.Parameters[0].ParameterType.FullName == "Game.ComponentPlayer"
            && item.HasBody);
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction originalStart = method.Body.Instructions[0];
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_1));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldc_I4_M1));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
        return true;
    }

    private static bool InjectBooleanHook(
        TypeDefinition type,
        string methodName,
        MethodReference bridge,
        IReadOnlyList<string> parameterTypeNames) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == methodName
            && item.ReturnType.FullName == "System.Void"
            && item.HasBody
            && item.Parameters.Count == parameterTypeNames.Count
            && item.Parameters
                .Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(parameterTypeNames));
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction originalStart = method.Body.Instructions[0];
        foreach (ParameterDefinition parameter in method.Parameters) {
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg, parameter));
        }
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
        return true;
    }

    private static bool InjectVoidBooleanHook(
        TypeDefinition type,
        string methodName,
        MethodReference bridge,
        IReadOnlyList<string> parameterTypeNames) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == methodName
            && item.ReturnType.FullName == "System.Void"
            && item.HasBody
            && item.Parameters.Count == parameterTypeNames.Count
            && item.Parameters
                .Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(parameterTypeNames));
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction originalStart = method.Body.Instructions[0];
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
        foreach (ParameterDefinition parameter in method.Parameters) {
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg, parameter));
        }
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
        return true;
    }

    private static bool InjectTrueReturningHook(
        TypeDefinition type,
        string methodName,
        MethodReference bridge,
        IReadOnlyList<string> parameterTypeNames) {
        if (type == null || bridge == null) {
            return false;
        }

        MethodDefinition method = type.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == methodName
            && item.ReturnType.FullName == "System.Boolean"
            && item.HasBody
            && item.Parameters.Count == parameterTypeNames.Count
            && item.Parameters
                .Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(parameterTypeNames));
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction originalStart = method.Body.Instructions[0];
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
        foreach (ParameterDefinition parameter in method.Parameters) {
            il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg, parameter));
        }
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
        return true;
    }

    private static MethodReference ResolveBridge(
        ModuleDefinition module,
        string methodName) {
        MethodInfo method = typeof(SushiToolCompatNetRuntime).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static);
        return method == null ? null : module.ImportReference(method);
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
