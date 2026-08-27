using System.Reflection;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Adds narrow request seams to Subnautica's mermaid interaction methods. The
/// optional assembly is rewritten only in memory and is never referenced by
/// the compatibility assembly's metadata.
/// </summary>
public static class SubnauticaCompatNetPatcher {
    public static bool Patch(ModAssemblyImage image) {
        if (image?.Bytes == null || image.Bytes.Length == 0) {
            return false;
        }

        using MemoryStream input = new(image.Bytes, writable: false);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            input,
            new ReaderParameters { InMemory = true, ReadSymbols = false });
        ModuleDefinition module = assembly.MainModule;

        MethodReference feedBridge = ResolveBridge(
            module,
            nameof(SubnauticaCompatNetRuntime.TryQueueMermaidFeed));
        MethodReference acceptBridge = ResolveBridge(
            module,
            nameof(SubnauticaCompatNetRuntime.QueueQuestAccept));
        MethodReference deliveryBridge = ResolveBridge(
            module,
            nameof(SubnauticaCompatNetRuntime.QueueQuestDelivery));
        bool changed = false;

        changed |= InjectMermaidFeedHook(
            FindType(assembly, "Subnautica.SubnauticaLoader"),
            feedBridge);

        TypeDefinition quest = FindType(assembly, "Subnautica.ComponentMermaidQuest");
        changed |= InjectVoidHook(
            quest,
            "TryAcceptQuest",
            acceptBridge,
            ["System.String"]);
        changed |= InjectVoidHook(
            quest,
            "TryDeliverItems",
            deliveryBridge,
            [typeof(ComponentPlayer).FullName]);

        if (!changed) {
            return false;
        }

        using MemoryStream output = new();
        assembly.Write(output, new WriterParameters { WriteSymbols = false });
        image.Bytes = output.ToArray();
        return true;
    }

    private static bool InjectMermaidFeedHook(
        TypeDefinition loader,
        MethodReference bridge) {
        if (loader == null || bridge == null) {
            return false;
        }

        MethodDefinition method = loader.Methods.FirstOrDefault(item =>
            !item.IsStatic
            && item.Name == "HandleMermaidInteraction"
            && item.ReturnType.FullName == "System.Void"
            && item.HasBody
            && item.Parameters.Count == 3
            && item.Parameters[0].ParameterType.FullName == typeof(ComponentPlayer).FullName
            && item.Parameters[1].ParameterType.FullName == typeof(ComponentCreature).FullName
            && item.Parameters[2].ParameterType.FullName == "Subnautica.ComponentMermaidAffection");
        if (method == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        ILProcessor il = method.Body.GetILProcessor();
        Instruction originalStart = method.Body.Instructions[0];
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_1));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_2));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_3));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
        il.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
        return true;
    }

    private static bool InjectVoidHook(
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
            && item.ReturnType.FullName != "System.Void"
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
        return true;
    }

    private static MethodReference ResolveBridge(
        ModuleDefinition module,
        string methodName) {
        MethodInfo method = typeof(SubnauticaCompatNetRuntime)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(candidate => candidate.Name == methodName);
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
