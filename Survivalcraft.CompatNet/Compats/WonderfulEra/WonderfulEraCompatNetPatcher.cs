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
        bool patched = false;
        patched |= PatchMethod(
            module,
            "WonderfulEra.WonderfulEraModLoader",
            "OnPlayerControlOtherMount",
            nameof(WonderfulEraCompatNetRuntime.TrySyncVehicleControl),
            candidate => !candidate.IsStatic
                && candidate.ReturnType.FullName == "System.Void"
                && candidate.HasBody
                && candidate.Parameters.Count == 3
                && candidate.Parameters[0].ParameterType.FullName == typeof(ComponentPlayer).FullName
                && candidate.Parameters[1].ParameterType.FullName == "System.Boolean"
                && candidate.Parameters[2].ParameterType.FullName == "System.Boolean&",
            (method, processor, bridge) => {
                Instruction originalStart = method.Body.Instructions[0];
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_1));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_2));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_3));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
            });
        patched |= ReplaceProjectileCalls(module, ResolveBridge(
            module,
            nameof(WonderfulEraCompatNetRuntime.FireProjectileBridge)));
        patched |= ReplaceGenericProjectileCalls(module, ResolveBridge(
            module,
            nameof(WonderfulEraCompatNetRuntime.FireProjectileGenericBridge)));
        patched |= PatchMethod(
            module,
            "WonderfulEra.ComponentRiderN",
            "StartMounting",
            nameof(WonderfulEraCompatNetRuntime.TrySyncRiderMounting),
            candidate => !candidate.IsStatic
                && candidate.ReturnType.FullName == "System.Void"
                && candidate.HasBody
                && candidate.Parameters.Count == 1
                && candidate.Parameters[0].ParameterType.FullName == typeof(ComponentMount).FullName,
            (method, processor, bridge) => {
                Instruction originalStart = method.Body.Instructions[0];
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_1));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
            });
        patched |= PatchMethod(
            module,
            "WonderfulEra.ComponentRiderN",
            "StartDismounting",
            nameof(WonderfulEraCompatNetRuntime.TrySyncRiderDismounting),
            candidate => !candidate.IsStatic
                && candidate.ReturnType.FullName == "System.Void"
                && candidate.HasBody
                && candidate.Parameters.Count == 0,
            (method, processor, bridge) => {
                Instruction originalStart = method.Body.Instructions[0];
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, bridge));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
            });
        if (!patched) {
            return false;
        }

        using MemoryStream output = new();
        assembly.Write(output, new WriterParameters { WriteSymbols = false });
        image.Bytes = output.ToArray();
        return true;
    }

    private static bool PatchMethod(
        ModuleDefinition module,
        string typeFullName,
        string methodName,
        string bridgeMethodName,
        Func<MethodDefinition, bool> predicate,
        Action<MethodDefinition, ILProcessor, MethodReference> inject) {
        MethodReference bridge = ResolveBridge(module, bridgeMethodName);
        TypeDefinition type = FindType(module.Assembly, typeFullName);
        MethodDefinition method = type?.Methods.FirstOrDefault(candidate =>
            !candidate.IsStatic
            && candidate.Name == methodName
            && predicate(candidate));
        if (method == null || bridge == null || ContainsBridgeCall(method, bridge)) {
            return false;
        }

        inject(method, method.Body.GetILProcessor(), bridge);
        return true;
    }

    public static bool PatchRiderDismounting(ModAssemblyImage image) {
        if (image?.Bytes == null || image.Bytes.Length == 0) {
            return false;
        }

        using MemoryStream input = new(image.Bytes, writable: false);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            input,
            new ReaderParameters { InMemory = true, ReadSymbols = false });
        MethodReference bridge = ResolveBridge(
            assembly.MainModule,
            nameof(WonderfulEraCompatNetRuntime.TrySyncRiderDismounting));
        bool patched = PatchMethod(
            assembly.MainModule,
            "Game.ComponentNewRider",
            "StartDismounting",
            nameof(WonderfulEraCompatNetRuntime.TrySyncRiderDismounting),
            candidate => !candidate.IsStatic
                && candidate.ReturnType.FullName == "System.Void"
                && candidate.HasBody
                && candidate.Parameters.Count == 0,
            (method, processor, methodBridge) => {
                Instruction originalStart = method.Body.Instructions[0];
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ldarg_0));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Call, methodBridge));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Brfalse, originalStart));
                processor.InsertBefore(originalStart, Instruction.Create(OpCodes.Ret));
            });
        if (!patched || bridge == null) {
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
        MethodInfo method = typeof(WonderfulEraCompatNetRuntime)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(candidate => candidate.Name == methodName);
        return method == null ? null : module.ImportReference(method);
    }

    private static bool ReplaceProjectileCalls(
        ModuleDefinition module,
        MethodReference bridge) {
        if (bridge == null) {
            return false;
        }

        bool changed = false;
        foreach (TypeDefinition type in module.Types.SelectMany(FlattenTypes)) {
            foreach (MethodDefinition method in type.Methods) {
                if (!method.HasBody) {
                    continue;
                }
                foreach (Instruction instruction in method.Body.Instructions) {
                    if (instruction.Operand is not MethodReference reference
                        || reference.Name != "FireProjectile"
                        || reference.DeclaringType.FullName != "Game.SubsystemProjectiles"
                        || reference.Parameters.Count != 5
                        || reference.GenericParameters.Count != 0) {
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

    private static bool ReplaceGenericProjectileCalls(
        ModuleDefinition module,
        MethodReference bridge) {
        if (bridge == null) {
            return false;
        }

        bool changed = false;
        foreach (TypeDefinition type in module.Types.SelectMany(FlattenTypes)) {
            foreach (MethodDefinition method in type.Methods) {
                if (!method.HasBody) {
                    continue;
                }
                foreach (Instruction instruction in method.Body.Instructions) {
                    if (instruction.Operand is not GenericInstanceMethod reference
                        || reference.Name != "FireProjectile"
                        || reference.DeclaringType.FullName != "Game.SubsystemProjectiles"
                        || reference.Parameters.Count != 5
                        || reference.GenericArguments.Count != 1) {
                        continue;
                    }

                    GenericInstanceMethod replacement = new(bridge);
                    replacement.GenericArguments.Add(
                        module.ImportReference(reference.GenericArguments[0]));
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = replacement;
                    changed = true;
                }
            }
        }
        return changed;
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
