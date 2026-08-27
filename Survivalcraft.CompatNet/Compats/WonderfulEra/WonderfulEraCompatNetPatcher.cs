using System.Reflection;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

/// <summary>
/// Adds a server-authoritative input seam to WonderfulEra's custom vehicle
/// control hook. The target assembly is patched in memory and remains an
/// optional runtime dependency.
/// </summary>
public static class WonderfulEraCompatNetPatcher {
    public static bool Patch(ModAssemblyImage image) {
        if (image?.Bytes == null || image.Bytes.Length == 0) {
            return false;
        }

        using MemoryStream input = new(image.Bytes, writable: false);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            input,
            new ReaderParameters { InMemory = true, ReadSymbols = false });
        ModuleDefinition module = assembly.MainModule;
        MethodReference bridge = ResolveBridge(
            module,
            nameof(WonderfulEraCompatNetRuntime.TrySyncVehicleControl));
        TypeDefinition loader = FindType(assembly, "WonderfulEra.WonderfulEraModLoader");
        MethodDefinition method = loader?.Methods.FirstOrDefault(candidate =>
            !candidate.IsStatic
            && candidate.Name == "OnPlayerControlOtherMount"
            && candidate.ReturnType.FullName == "System.Void"
            && candidate.HasBody
            && candidate.Parameters.Count == 3
            && candidate.Parameters[0].ParameterType.FullName == typeof(ComponentPlayer).FullName
            && candidate.Parameters[1].ParameterType.FullName == "System.Boolean"
            && candidate.Parameters[2].ParameterType.FullName == "System.Boolean&");
        if (method == null || bridge == null || ContainsBridgeCall(method, bridge)) {
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

        using MemoryStream output = new();
        assembly.Write(output, new WriterParameters { WriteSymbols = false });
        image.Bytes = output.ToArray();
        return true;
    }

    private static MethodReference ResolveBridge(
        ModuleDefinition module,
        string methodName) {
        MethodInfo method = typeof(WonderfulEraCompatNetRuntime)
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
