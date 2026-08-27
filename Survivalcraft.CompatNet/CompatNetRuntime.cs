using Engine;
using Game.Network;
using GameEntitySystem;
using System.Reflection;

namespace Game;

public static class CompatNetRuntime {
    private static MethodInfo s_addMissileMethod;

    public static bool NetworkBridgeReady => CompatNetCore.NetworkBridgeReady;

    public static void InitializeNetwork() {
        CompatNetCore.InitializeNetwork();
    }

    public static Projectile FireProjectileBridge(
        SubsystemProjectiles subsystemProjectiles,
        int value,
        Vector3 position,
        Vector3 velocity,
        Vector3 angularVelocity,
        ComponentCreature owner) {
        if (!IsClientRunning()) {
            return subsystemProjectiles.FireProjectile(
                value,
                position,
                velocity,
                angularVelocity,
                owner);
        }

        ComponentPlayer player = owner?.Entity.FindComponent<ComponentPlayer>();
        if (player == null) {
            return null;
        }

        GunProjectileRequest request = new() {
            BulletValue = value,
            Position = position,
            Velocity = velocity,
            AngularVelocity = angularVelocity
        };
        CompatNetCore.QueueRequest(
            player,
            GunCompatNetAdapter.StableAdapterId,
            (ushort)GunCompatNetOperation.Projectile,
            CompatNetPayload.Write(request.Serialize));

        return null;
    }

    public static bool TryQueueMissile(
        Vector3 position,
        Quaternion rotation,
        Project project,
        float remainTime,
        float directlyTime,
        int guideLevel,
        bool antiVehi,
        ComponentBody target,
        ComponentCreature owner,
        Vector3 velocity,
        float startEngineTime) {
        if (!IsClientRunning()) {
            return false;
        }

        ComponentPlayer player = owner?.Entity.FindComponent<ComponentPlayer>();
        if (player == null) {
            // Client-side AI/vehicle code must not create a second local missile;
            // its authoritative server-side entity will arrive through EntityLoad.
            return true;
        }

        GunMissileRequest request = new() {
            Position = position,
            Rotation = rotation,
            RemainTime = remainTime,
            DirectlyTime = directlyTime,
            GuideLevel = guideLevel,
            AntiVehi = antiVehi,
            TargetEntityId = target?.Entity?.Id ?? 0,
            Velocity = velocity,
            StartEngineTime = startEngineTime
        };
        CompatNetCore.QueueRequest(
            player,
            GunCompatNetAdapter.StableAdapterId,
            (ushort)GunCompatNetOperation.Missile,
            CompatNetPayload.Write(request.Serialize));
        return true;
    }

    private static bool IsClientRunning() {
        return NetworkManager.IsClientRunning;
    }

    public static void FireServerProjectile(
        int value,
        Vector3 position,
        Vector3 velocity,
        Vector3 angularVelocity,
        ComponentCreature owner) {
        SubsystemProjectiles subsystemProjectiles =
            GameManager.Project.FindSubsystem<SubsystemProjectiles>();

        Projectile projectile = subsystemProjectiles.FireProjectile(
            value,
            position,
            velocity,
            angularVelocity,
            owner);

        if (projectile == null) {
            return;
        }

        ApplyProjectileDefaults(subsystemProjectiles, projectile);
    }

    public static void FireServerMissile(
        Vector3 position,
        Quaternion rotation,
        float remainTime,
        float directlyTime,
        int guideLevel,
        bool antiVehi,
        int targetEntityId,
        Vector3 velocity,
        float startEngineTime,
        ComponentCreature owner) {
        ComponentBody target = GameManager.Project.FindEntity(targetEntityId)
            ?.FindComponent<ComponentBody>();

        MethodInfo method = ResolveAddMissileMethod();
        if (method == null) {
            Log.Error("[Survivalcraft.CompatNet] Cannot find Game.Functions.AddMissile for server request.");
            return;
        }

        try {
            method.Invoke(null, [
                position,
                rotation,
                GameManager.Project,
                remainTime,
                directlyTime,
                guideLevel,
                antiVehi,
                target,
                owner,
                velocity,
                startEngineTime
            ]);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to create server missile: {e}");
        }
    }

    private static MethodInfo ResolveAddMissileMethod() {
        if (s_addMissileMethod != null) {
            return s_addMissileMethod;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            Type type = assembly.GetType("Game.Functions", throwOnError: false);
            if (type == null || type.Assembly == typeof(CompatNetRuntime).Assembly) {
                continue;
            }

            MethodInfo method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(item => item.Name == "AddMissile" && item.GetParameters().Length == 11);
            if (method != null) {
                s_addMissileMethod = method;
                return method;
            }
        }

        return null;
    }

    public static bool IsGunProjectile(int value) {
        return Terrain.ExtractContents(value) == 603;
    }

    public static void ApplyProjectileDefaults(
        SubsystemProjectiles subsystemProjectiles,
        Projectile projectile) {
        if (!IsGunProjectile(projectile.Value)) {
            return;
        }

        projectile.ProjectileStoppedAction = ProjectileStoppedAction.Disappear;
    }
}
