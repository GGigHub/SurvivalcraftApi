using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Game;

internal static class Compat23GunModPatcher {
    public static bool MakeBlockInitializationRepeatable(ModAssemblyImage image) {
        ArgumentNullException.ThrowIfNull(image);
        using MemoryStream source = new(image.Bytes, writable: false);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            source,
            new ReaderParameters {
                InMemory = true,
                ReadingMode = ReadingMode.Immediate
            });

        bool changed = InsertDictionaryClear(
            assembly.MainModule,
            "Game.Functions",
            "AllSteps",
            "Initialize");
        changed |= InsertDictionaryClear(
            assembly.MainModule,
            "Game.SAFunctions",
            "TechTree",
            "InitializeTechTree");
        changed |= InsertDictionaryClear(
            assembly.MainModule,
            "Game.SAFunctions",
            "MaterialsOfVehicle",
            "InitializeMaterialsOfVehicle");
        changed |= InsertDictionaryClear(
            assembly.MainModule,
            "Game.SAQuestManager",
            "AllQuests",
            "Initialize");
        changed |= InsertDictionaryClear(
            assembly.MainModule,
            "Game.MapManager",
            "Settings",
            "LoadMapSettings");
        changed |= ReplaceDictionaryAddsWithSetters(
            assembly.MainModule,
            "Game.MapManager",
            "LoadMapSettings");
        if (!changed) {
            return false;
        }

        using MemoryStream target = new();
        assembly.Write(target);
        image.Bytes = target.ToArray();
        return true;
    }

    private static bool InsertDictionaryClear(
        ModuleDefinition module,
        string typeName,
        string fieldName,
        string methodName) {
        TypeDefinition type = module.Types.FirstOrDefault(
            candidate => string.Equals(candidate.FullName, typeName, StringComparison.Ordinal));
        FieldDefinition field = type?.Fields.FirstOrDefault(
            candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal)
                && candidate.IsStatic
                && candidate.FieldType is GenericInstanceType genericType
                && string.Equals(
                    genericType.ElementType.FullName,
                    "System.Collections.Generic.Dictionary`2",
                    StringComparison.Ordinal));
        MethodDefinition method = type?.Methods.FirstOrDefault(
            candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && candidate.IsStatic
                && !candidate.HasParameters
                && candidate.ReturnType.MetadataType == MetadataType.Void);
        if (field == null || method?.HasBody != true) {
            throw new MissingMethodException(
                $"Could not find {typeName}.{fieldName} and parameterless {typeName}.{methodName}().");
        }

        Mono.Collections.Generic.Collection<Instruction> instructions = method.Body.Instructions;
        if (instructions.Count >= 2
            && instructions[0].OpCode == OpCodes.Ldsfld
            && ReferenceEquals(instructions[0].Operand, field)
            && instructions[1].OpCode == OpCodes.Callvirt
            && instructions[1].Operand is MethodReference existingCall
            && string.Equals(existingCall.Name, "Clear", StringComparison.Ordinal)) {
            return false;
        }
        if (instructions.Count == 0) {
            throw new InvalidDataException($"{typeName}.{methodName}() has an empty method body.");
        }

        GenericInstanceType dictionaryType = (GenericInstanceType)field.FieldType;
        MethodReference clearMethod = new("Clear", module.TypeSystem.Void, dictionaryType) {
            HasThis = true,
            CallingConvention = MethodCallingConvention.Default
        };
        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = instructions[0];
        Instruction loadDictionary = il.Create(OpCodes.Ldsfld, field);
        Instruction clearDictionary = il.Create(OpCodes.Callvirt, clearMethod);
        il.InsertBefore(first, loadDictionary);
        il.InsertAfter(loadDictionary, clearDictionary);
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 1);
        return true;
    }

    private static bool ReplaceDictionaryAddsWithSetters(
        ModuleDefinition module,
        string typeName,
        string methodName) {
        TypeDefinition type = module.Types.FirstOrDefault(
            candidate => string.Equals(candidate.FullName, typeName, StringComparison.Ordinal));
        MethodDefinition method = type?.Methods.FirstOrDefault(
            candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && candidate.IsStatic
                && !candidate.HasParameters
                && candidate.ReturnType.MetadataType == MetadataType.Void);
        if (method?.HasBody != true) {
            throw new MissingMethodException($"Could not find parameterless {typeName}.{methodName}().");
        }

        bool changed = false;
        foreach (Instruction instruction in method.Body.Instructions) {
            if (instruction.Operand is not MethodReference addMethod
                || !string.Equals(addMethod.Name, "Add", StringComparison.Ordinal)
                || addMethod.DeclaringType is not GenericInstanceType genericType
                || !string.Equals(
                    genericType.ElementType.FullName,
                    "System.Collections.Generic.Dictionary`2",
                    StringComparison.Ordinal)
                || addMethod.Parameters.Count != 2) {
                continue;
            }

            MethodReference setter = new("set_Item", module.TypeSystem.Void, addMethod.DeclaringType) {
                HasThis = addMethod.HasThis,
                ExplicitThis = addMethod.ExplicitThis,
                CallingConvention = addMethod.CallingConvention
            };
            foreach (ParameterDefinition parameter in addMethod.Parameters) {
                setter.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            }
            instruction.Operand = setter;
            changed = true;
        }
        return changed;
    }
}
