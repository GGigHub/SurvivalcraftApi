using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Runtime bridge called by the Cecil-patched Enchantment image.
/// This class deliberately has no static reference to Enchantment types.
/// </summary>
public static class EnchantmentCompatNetRuntime {
    private const string RollMethodName = "RollEnchantment";
    private static double s_nextServerRefresh;
    private static readonly Dictionary<IInventory, int[]> s_inventorySnapshots =
        new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<PlayerData, float> s_levelSnapshots =
        new(ReferenceEqualityComparer.Instance);

    public static void Reset() {
        s_nextServerRefresh = 0;
        s_inventorySnapshots.Clear();
        s_levelSnapshots.Clear();
    }

    /// <summary>
    /// On a client-only process, sends a compatibility request and prevents the
    /// local call. A server or single-player process invokes the original method.
    /// </summary>
    public static void RollEnchantmentBridge(object platform) {
        if (!IsClientOnly()) {
            InvokeOriginalRoll(platform);
            return;
        }

        // The local client must not roll again when Multiplayer later applies the
        // server-authoritative item/inventory result. If the owner cannot be
        // resolved, still suppress the local call rather than execute it twice.
        ComponentPlayer player = ResolvePlayer(platform);
        if (player?.PlayerData?.IsMainPlayer == true) {
            int entityId = (platform as Component)?.Entity?.Id ?? 0;
            if (entityId <= 0) {
                return;
            }

            EnchantmentRollRequest request = new() {
                EntityId = entityId
            };
            CompatNetCore.QueueRequest(
                player,
                EnchantmentCompatNetAdapter.StableAdapterId,
                (ushort)EnchantmentCompatNetOperation.RollEnchantment,
                CompatNetPayload.Write(request.Serialize));
        }
    }

    public static bool TryExecuteServerRoll(int entityId, ComponentPlayer requester) {
        if (!NetworkManager.IsServerRunning
            || entityId <= 0
            || requester?.Entity == null) {
            return false;
        }

        Entity entity = GameManager.Project?.FindEntity(entityId);
        Component platform = entity?.Components?.FirstOrDefault(component =>
            string.Equals(
                component.GetType().FullName,
                "Enchantment.ComponentMagicRemovalPlatform",
                StringComparison.Ordinal));
        if (platform == null || !IsWithinUseDistance(requester, platform)) {
            platform = FindInteractingPlatform(requester);
        }
        if (platform == null || !IsWithinUseDistance(requester, platform)) {
            return false;
        }

        MethodInfo interactingPlayerMethod = FindMethod(platform.GetType(), "FindInteractingPlayer", 0);
        if (interactingPlayerMethod == null) {
            return false;
        }

        try {
            if (interactingPlayerMethod.Invoke(platform, null) is not ComponentPlayer interactingPlayer
                || !ReferenceEquals(interactingPlayer, requester)) {
                return false;
            }
        }
        catch {
            // Interaction ownership is a mandatory authority check. If the
            // target version cannot expose it, fail closed instead of falling
            // back to distance alone.
            return false;
        }

        InvokeOriginalRoll(platform);
        QueueRollCorrections(platform, requester);
        return true;
    }

    public static void OnServerUpdate() {
        if (!NetworkManager.IsServerRunning || GameManager.Project == null
            || Time.RealTime < s_nextServerRefresh) {
            return;
        }
        s_nextServerRefresh = Time.RealTime + 0.25;

        HashSet<IInventory> seen = new(ReferenceEqualityComparer.Instance);
        foreach (Entity entity in GameManager.Project.Entities) {
            foreach (IInventory inventory in entity.FindComponents<IInventory>()) {
                seen.Add(inventory);
                int[] current = CaptureInventory(inventory);
                if (!s_inventorySnapshots.TryGetValue(inventory, out int[] previous)
                    || !current.SequenceEqual(previous)) {
                    s_inventorySnapshots[inventory] = current;
                    InventoryPacketHelpers.QueueInventorySync(inventory);
                }
            }

            ComponentPlayer player = entity.FindComponent<ComponentPlayer>();
            if (player?.PlayerData == null) {
                continue;
            }
            float level = player.PlayerData.Level;
            if (!s_levelSnapshots.TryGetValue(player.PlayerData, out float previousLevel)
                || previousLevel != level) {
                s_levelSnapshots[player.PlayerData] = level;
                NetworkManager.Queue(new PlayerAddExperiencePacket(
                    player.PlayerData,
                    0,
                    false,
                    level));
            }
        }

        foreach (IInventory stale in s_inventorySnapshots.Keys.Where(item => !seen.Contains(item)).ToArray()) {
            s_inventorySnapshots.Remove(stale);
        }
    }

    /// <summary>
    /// The repair subsystem scans world experience orbs and directly mutates
    /// player inventory/clothing. It has no discrete client input to request.
    /// On a client-only process it must therefore remain dormant while the
    /// server performs the scan and normal multiplayer sync carries the result.
    /// </summary>
    public static bool ShouldSkipMendingRepairUpdate() {
        return IsClientOnly();
    }

    /// <summary>
    /// Fishing float physics, bite RNG and loot generation are all private
    /// state in the Enchantment subsystem and have no safe request payload yet.
    /// Suppress client simulation so it cannot independently create loot.
    /// </summary>
    public static bool ShouldSkipFishingFloatUpdate() {
        return IsClientOnly();
    }

    /// <summary>
    /// The recall behavior is implemented as a Harmony death prefix inside the
    /// third-party mod. Let the normal game death path run on a client-only
    /// process; only a server-side death can start the custom recall ritual.
    /// </summary>
    public static bool ShouldSkipRecallPatch() {
        return IsClientOnly();
    }

    private static bool IsClientOnly() {
        return NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
    }

    private static ComponentPlayer ResolvePlayer(object platform) {
        return ResolveInteractingPlayer(platform);
    }

    private static ComponentPlayer ResolveInteractingPlayer(object inventory) {
        if (inventory == null) {
            return null;
        }

        MethodInfo method = FindMethod(inventory.GetType(), "FindInteractingPlayer", 0);
        if (method != null) {
            try {
                if (method.Invoke(inventory, null) is ComponentPlayer player) {
                    return player;
                }
            }
            catch {
                // Fall through to an entity-owned player, if present.
            }
        }

        if (inventory is Component component && component.Entity != null) {
            return component.Entity.FindComponent<ComponentPlayer>();
        }

        return null;
    }

    private static void InvokeOriginalRoll(object platform) {
        if (platform == null) {
            return;
        }

        MethodInfo method = FindRollMethod(platform.GetType());
        if (method == null) {
            return;
        }

        try {
            method.Invoke(platform, null);
        }
        catch (Exception e) {
            // A version-skewed or malformed target must not escape the bridge.
            Log.Error($"[Survivalcraft.CompatNet] Failed to invoke {RollMethodName}: {e}");
        }
    }

    private static bool IsWithinUseDistance(ComponentPlayer requester, Component platform) {
        ComponentBody playerBody = requester.Entity.FindComponent<ComponentBody>();
        ComponentFrame platformFrame = platform.Entity?.FindComponent<ComponentFrame>();
        if (playerBody == null || platformFrame == null) {
            return false;
        }

        return Vector3.DistanceSquared(playerBody.Position, platformFrame.Position) <= 64f;
    }

    private static Component FindInteractingPlatform(ComponentPlayer requester) {
        if (GameManager.Project == null) {
            return null;
        }

        foreach (Entity entity in GameManager.Project.Entities) {
            Component platform = entity.Components.FirstOrDefault(component =>
                string.Equals(
                    component.GetType().FullName,
                    "Enchantment.ComponentMagicRemovalPlatform",
                    StringComparison.Ordinal));
            if (platform == null || !IsWithinUseDistance(requester, platform)) {
                continue;
            }

            MethodInfo method = FindMethod(platform.GetType(), "FindInteractingPlayer", 0);
            try {
                if (method?.Invoke(platform, null) is ComponentPlayer player
                    && ReferenceEquals(player, requester)) {
                    return platform;
                }
            }
            catch {
            }
        }
        return null;
    }

    private static void QueueRollCorrections(Component platform, ComponentPlayer requester) {
        if (platform is IInventory inventory) {
            InventoryPacketHelpers.QueueInventorySync(inventory);
        }
        if (requester?.PlayerData != null) {
            NetworkManager.Queue(new PlayerAddExperiencePacket(
                requester.PlayerData,
                0,
                false,
                requester.PlayerData.Level));
        }
    }

    private static int[] CaptureInventory(IInventory inventory) {
        int[] values = new int[inventory.SlotsCount * 2];
        for (int i = 0; i < inventory.SlotsCount; i++) {
            values[i * 2] = inventory.GetSlotValue(i);
            values[i * 2 + 1] = inventory.GetSlotCount(i);
        }
        return values;
    }

    private static MethodInfo FindMethod(Type type, string name, int parameterCount) {
        for (Type current = type; current != null; current = current.BaseType) {
            MethodInfo method = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => candidate.Name == name
                    && !candidate.IsStatic
                    && candidate.GetParameters().Length == parameterCount);
            if (method != null) {
                return method;
            }
        }

        return null;
    }

    private static MethodInfo FindRollMethod(Type type) {
        for (Type current = type; current != null; current = current.BaseType) {
            MethodInfo method = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate =>
                    candidate.Name == RollMethodName
                    && !candidate.IsStatic
                    && candidate.ReturnType == typeof(void)
                    && candidate.GetParameters().Length == 0);
            if (method != null) {
                return method;
            }
        }

        return null;
    }
}
