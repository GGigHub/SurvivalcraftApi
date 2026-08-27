using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// CompatNet adapter for gunsmod.ysf.scmod.
/// </summary>
public sealed class GunCompatNetAdapter : ICompatNetAdapter {
    public const ushort StableAdapterId = 1;
    public const string TargetPackageName = "gunsmod.ysf.scmod";

    private const string FunctionsTypeName = "Game.Functions";
    private const string GunsUserTypeName = "Game.ComponentGunsUser";

    public ushort AdapterId => StableAdapterId;
    public string AdapterName => TargetPackageName;

    public void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        foreach (ModAssemblyImage image in assemblies) {
            if (!string.Equals(image.Owner.modInfo?.PackageName, TargetPackageName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            try {
                if (GunModPatcher.Patch(image)) {
                    Log.Information($"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                }
            }
            catch (Exception e) {
                Log.Error($"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
            }
        }
    }

    public void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        ComponentCreature owner = playerData.ComponentPlayer;
        if (packet.Operation == (ushort)GunCompatNetOperation.Projectile) {
            GunProjectileRequest request = new();
            CompatNetPayload.Read(packet.Payload, request.Serialize);
            CompatNetRuntime.FireServerProjectile(
                request.BulletValue,
                request.Position,
                request.Velocity,
                request.AngularVelocity,
                owner);
        }
        else if (packet.Operation == (ushort)GunCompatNetOperation.Missile) {
            GunMissileRequest request = new();
            CompatNetPayload.Read(packet.Payload, request.Serialize);
            CompatNetRuntime.FireServerMissile(
                request.Position,
                request.Rotation,
                request.RemainTime,
                request.DirectlyTime,
                request.GuideLevel,
                request.AntiVehi,
                request.TargetEntityId,
                request.Velocity,
                request.StartEngineTime,
                owner);
        }
    }

    public void OnProjectileAdded(
        SubsystemProjectiles subsystemProjectiles,
        ref Projectile projectile,
        ValuesDictionary loadValuesDictionary) {
        if (CompatNetRuntime.IsGunProjectile(projectile.Value)) {
            CompatNetRuntime.ApplyProjectileDefaults(subsystemProjectiles, projectile);
        }
    }

    private static class GunModPatcher {
        public static bool Patch(ModAssemblyImage image) {
            using MemoryStream input = new(image.Bytes, writable: false);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                input,
                new ReaderParameters { InMemory = true, ReadSymbols = false });

            ModuleDefinition module = assembly.MainModule;
            MethodReference bridge = ResolveBridge(module);
            MethodReference missileBridge = ResolveMissileBridge(module);
            bool changed = false;

            TypeDefinition functions = FindType(assembly, FunctionsTypeName);
            if (functions != null) {
                changed |= ReplaceProjectileCalls(functions, bridge);
                changed |= InjectMissileRequest(functions, missileBridge);
            }

            TypeDefinition gunsUser = FindType(assembly, GunsUserTypeName);
            if (gunsUser != null) {
                changed |= ReplaceProjectileCalls(gunsUser, bridge);
            }

            if (!changed) {
                return false;
            }

            using MemoryStream output = new();
            assembly.Write(output, new WriterParameters { WriteSymbols = false });
            image.Bytes = output.ToArray();
            return true;
        }

        private static MethodReference ResolveBridge(ModuleDefinition module) {
            MethodInfo method = typeof(CompatNetRuntime).GetMethod(
                nameof(CompatNetRuntime.FireProjectileBridge),
                BindingFlags.Public | BindingFlags.Static);

            return module.ImportReference(method);
        }

        private static MethodReference ResolveMissileBridge(ModuleDefinition module) {
            MethodInfo method = typeof(CompatNetRuntime).GetMethod(
                nameof(CompatNetRuntime.TryQueueMissile),
                BindingFlags.Public | BindingFlags.Static);

            return module.ImportReference(method);
        }

        private static bool ReplaceProjectileCalls(
            TypeDefinition type,
            MethodReference bridge) {
            bool changed = false;
            foreach (MethodDefinition method in type.Methods) {
                if (!method.HasBody) {
                    continue;
                }

                foreach (Instruction instruction in method.Body.Instructions) {
                    if (instruction.Operand is not MethodReference reference
                        || reference.Name != "FireProjectile"
                        || reference.DeclaringType.FullName != "Game.SubsystemProjectiles"
                        || reference.Parameters.Count != 5) {
                        continue;
                    }

                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = bridge;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool InjectMissileRequest(
            TypeDefinition type,
            MethodReference bridge) {
            MethodDefinition method = type.Methods.FirstOrDefault(item =>
                item.Name == "AddMissile"
                && item.IsStatic
                && item.ReturnType.FullName == "System.Void"
                && item.Parameters.Count == 11);
            if (method == null || !method.HasBody) {
                return false;
            }

            if (method.Body.Instructions.Any(item =>
                item.Operand is MethodReference reference
                && reference.Name == nameof(CompatNetRuntime.TryQueueMissile))) {
                return false;
            }

            ILProcessor il = method.Body.GetILProcessor();
            Instruction originalStart = method.Body.Instructions[0];
            Instruction returnInstruction = Instruction.Create(OpCodes.Ret);
            Instruction continueInstruction = Instruction.Create(OpCodes.Brfalse_S, originalStart);

            List<Instruction> prefix = [
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Ldarg_2),
                Instruction.Create(OpCodes.Ldarg_3),
                Instruction.Create(OpCodes.Ldarg, method.Parameters[4]),
                Instruction.Create(OpCodes.Ldarg, method.Parameters[5]),
                Instruction.Create(OpCodes.Ldarg, method.Parameters[6]),
                Instruction.Create(OpCodes.Ldarg, method.Parameters[7]),
                Instruction.Create(OpCodes.Ldarg, method.Parameters[8]),
                Instruction.Create(OpCodes.Ldarg, method.Parameters[9]),
                Instruction.Create(OpCodes.Ldarg, method.Parameters[10]),
                Instruction.Create(OpCodes.Call, bridge),
                continueInstruction,
                returnInstruction
            ];

            foreach (Instruction instruction in prefix) {
                il.InsertBefore(originalStart, instruction);
            }

            return true;
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
