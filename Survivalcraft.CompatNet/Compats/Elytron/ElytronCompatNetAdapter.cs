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
        // Elytron exposes no request protocol: the owner's client owns its own
        // gliding body and the standard PlayerBodyUpdatePacket already carries
        // the resulting position, rotation and velocity to the server and to
        // observers. Unknown operations are intentionally ignored.
    }

    private static class ElytronCompatNetPatcher {
        private const string GlidingTypeName = "Elytron.ComponentGliding";
        private const string UpdateMethodName = "Update";

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
            if (skipMethod == null) {
                return false;
            }

            MethodReference skipBridge = module.ImportReference(skipMethod);

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

            if (!changed) {
                return false;
            }
            using MemoryStream output = new();
            assembly.Write(output, new WriterParameters { WriteSymbols = false });
            image.Bytes = output.ToArray();
            return true;
        }

        private static bool HasCall(MethodDefinition method, MethodReference bridge) {
            return method.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference reference
                && reference.Name == bridge.Name
                && reference.DeclaringType.FullName == bridge.DeclaringType.FullName);
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