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
    private const string TargetAssemblyName = "#Guns.dll";

    private const string FunctionsTypeName = "Game.Functions";
    private const string GunsUserTypeName = "Game.ComponentGunsUser";

    private bool m_targetPackageLoaded;
    private double m_nextStatePublishTime;
    private double m_nextFullRefreshTime;
    private int m_lastAppliedStateSequence;
    private readonly Dictionary<string, byte[]> m_lastStatePayloads = [];
    private double m_nextInventoryPublishTime;
    private double m_nextInventoryFullRefreshTime;
    private int m_inventorySequence;
    private int m_lastAppliedInventorySequence;
    private readonly Dictionary<string, byte[]> m_lastInventoryPayloads = [];

    public ushort AdapterId => StableAdapterId;
    public string AdapterName => TargetPackageName;

    public void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        m_targetPackageLoaded = false;
        m_nextStatePublishTime = 0;
        m_nextFullRefreshTime = 0;
        m_lastAppliedStateSequence = 0;
        m_lastStatePayloads.Clear();
        m_nextInventoryPublishTime = 0;
        m_nextInventoryFullRefreshTime = 0;
        m_inventorySequence = 0;
        m_lastAppliedInventorySequence = 0;
        m_lastInventoryPayloads.Clear();
        foreach (ModAssemblyImage image in assemblies) {
            if (!string.Equals(image.Owner.modInfo?.PackageName, TargetPackageName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            m_targetPackageLoaded = true;

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

    public void HandleBroadcast(CompatNetPacket packet) {
        if (packet?.Operation == (ushort)CompatNetStateOperation.StateSnapshot
            && m_targetPackageLoaded
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            CompatNetStateRuntime.Apply(
                packet.Payload,
                TargetAssemblyName,
                ScopedUpdateGuardScope.AllEntitiesExceptLocalPlayer,
                ref m_lastAppliedStateSequence);
        }
        else if (packet?.Operation == (ushort)CompatNetInventoryOperation.StateSnapshot
            && m_targetPackageLoaded
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            CompatNetInventoryRuntime.Apply(
                packet.Payload,
                TargetAssemblyName,
                ref m_lastAppliedInventorySequence);
        }
    }

    public void OnServerUpdate(float dt) {
        CompatNetStateRuntime.Publish(
            StableAdapterId,
            TargetAssemblyName,
            ScopedUpdateGuardScope.AllEntitiesExceptLocalPlayer,
            m_targetPackageLoaded,
            ref m_nextStatePublishTime,
            ref m_nextFullRefreshTime,
            m_lastStatePayloads);
        CompatNetInventoryRuntime.Publish(
            StableAdapterId,
            TargetAssemblyName,
            m_targetPackageLoaded,
            ref m_nextInventoryPublishTime,
            ref m_nextInventoryFullRefreshTime,
            ref m_inventorySequence,
            m_lastInventoryPayloads);
    }

    public bool ShouldSkipClientUpdate(IUpdateable updateable) {
        if (!m_targetPackageLoaded
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || updateable == null
            || !IsTargetAssembly(updateable.GetType().Assembly)) {
            return false;
        }

        if (updateable is not Component component || component.Entity == null) {
            return false;
        }

        ComponentPlayer player = component.Entity.FindComponent<ComponentPlayer>();
        return player?.PlayerData?.IsMainPlayer != true;
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

    private static bool IsTargetAssembly(Assembly assembly) {
        if (assembly == null) {
            return false;
        }
        return NamesEqual(assembly.GetName().Name, TargetAssemblyName)
            || NamesEqual(assembly.ManifestModule?.Name, TargetAssemblyName);
    }

    private static bool NamesEqual(string left, string right) {
        return string.Equals(
            NormalizeName(left),
            NormalizeName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        string result = value.Replace('\\', '/');
        int separator = result.LastIndexOf('/');
        if (separator >= 0) {
            result = result[(separator + 1)..];
        }
        return result.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? result[..^4]
            : result;
    }
}
