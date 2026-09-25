using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Converts Neorxna's local home/back-position mutations into authenticated
/// CompatNet requests. The optional assembly is rewritten only in memory.
/// </summary>
public static class NeorxnaCompatNetPatcher {
    public static bool Patch(ModAssemblyImage image) {
        if (image?.Bytes == null || image.Bytes.Length == 0) {
            return false;
        }

        using MemoryStream input = new(image.Bytes, writable: false);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            input,
            new ReaderParameters { InMemory = true, ReadSymbols = false });
        ModuleDefinition module = assembly.MainModule;
        bool changed = false;

        MethodReference setHomeBridge = ResolveBridge(
            module,
            nameof(NeorxnaCompatNetRuntime.TryQueueSetHome));
        MethodReference deleteHomeBridge = ResolveBridge(
            module,
            nameof(NeorxnaCompatNetRuntime.TryQueueDeleteHome));
        MethodReference backPositionBridge = ResolveBridge(
            module,
            nameof(NeorxnaCompatNetRuntime.TryQueueSetBackPosition));
        TypeDefinition debugDrawing = FindType(
            assembly,
            "Neorxna.Components.ComponentDebugDrawing");

        changed |= InjectVoidBooleanGate(
            debugDrawing,
            "SetHome",
            setHomeBridge,
            ["System.String"]);
        changed |= InjectVoidBooleanGate(
            debugDrawing,
            "DeleteHome",
            deleteHomeBridge,
            ["System.String"]);
        changed |= InjectVoidBooleanGate(
            debugDrawing,
            "SetBackPosition",
            backPositionBridge,
            []);

        if (!changed) {
            return false;
        }

        using MemoryStream output = new();
        assembly.Write(output, new WriterParameters { WriteSymbols = false });
        image.Bytes = output.ToArray();
        return true;
    }

    private static bool InjectVoidBooleanGate(
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

    private static MethodReference ResolveBridge(
        ModuleDefinition module,
        string methodName) {
        MethodInfo method = typeof(NeorxnaCompatNetRuntime)
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
