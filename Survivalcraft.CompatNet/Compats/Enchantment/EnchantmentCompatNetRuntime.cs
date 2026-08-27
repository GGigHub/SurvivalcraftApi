using System.Reflection;
using Engine;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Runtime bridge called by the Cecil-patched Enchantment image.
/// This class deliberately has no static reference to Enchantment types.
/// </summary>
public static class EnchantmentCompatNetRuntime {
    private const string RollMethodName = "RollEnchantment";
    private const string RemoveSlotItemsMethodName = "RemoveSlotItems";
    private const string EnchantingTableTypeName = "Enchantment.ComponentEnchantingTable";
    private const string MagicRemovalPlatformTypeName = "Enchantment.ComponentMagicRemovalPlatform";

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
        return true;
    }

    /// <summary>
    /// Called at the start of ComponentEnchantingTable.RemoveSlotItems by the
    /// in-memory patch. A client never mutates the table locally: it sends only
    /// the table entity ID and the fixed result-slot operation. Returning true
    /// tells the patched method to return zero immediately.
    /// </summary>
    public static bool TryQueueEnchantingTableResult(
        object table,
        int slotIndex,
        int count) {
        if (!IsClientOnly()) {
            return false;
        }

        // The mod exposes exactly one result slot and its result count is one.
        // Invalid client arguments are suppressed too, rather than allowing a
        // local-only inventory mutation to escape the server authority model.
        if (slotIndex != 2) {
            // Input-slot removals are also used by normal inventory sync and
            // must continue through the original method on the client.
            return false;
        }
        if (count != 1) {
            return true;
        }

        ComponentPlayer player = ResolveInteractingPlayer(table);
        int entityId = (table as Component)?.Entity?.Id ?? 0;
        if (player?.PlayerData?.IsMainPlayer == true && entityId > 0) {
            EnchantingTableResultRequest request = new() {
                EntityId = entityId,
                SlotIndex = slotIndex,
                Count = count
            };
            CompatNetCore.QueueRequest(
                player,
                EnchantmentCompatNetAdapter.StableAdapterId,
                (ushort)EnchantmentCompatNetOperation.RemoveEnchantingTableResult,
                CompatNetPayload.Write(request.Serialize));
        }

        return true;
    }

    /// <summary>
    /// Server-side executor for the enchanting table result request. All
    /// meaningful state (interacting player, inventory slots, item validity,
    /// experience and final mutation) is re-evaluated by the original method.
    /// </summary>
    public static bool TryExecuteServerEnchantingTableResult(
        int entityId,
        int slotIndex,
        int count,
        ComponentPlayer requester) {
        if (!NetworkManager.IsServerRunning
            || entityId <= 0
            || slotIndex != 2
            || count != 1
            || requester?.Entity == null) {
            return false;
        }

        Entity entity = GameManager.Project?.FindEntity(entityId);
        Component table = entity?.Components?.FirstOrDefault(component =>
            string.Equals(
                component.GetType().FullName,
                EnchantingTableTypeName,
                StringComparison.Ordinal));
        if (table == null
            || !IsWithinUseDistance(requester, table)
            || !ReferenceEquals(ResolveInteractingPlayer(table), requester)
            || !HasValidEnchantingTableResult(table)) {
            return false;
        }

        MethodInfo method = FindRemoveSlotItemsMethod(table.GetType());
        if (method == null) {
            return false;
        }

        try {
            method.Invoke(table, [slotIndex, count]);
            return true;
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to execute enchanting table request: {e}");
            return false;
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

    private static bool HasValidEnchantingTableResult(Component table) {
        if (table == null) {
            return false;
        }

        // ComponentInventoryBase is intentionally accessed by reflection so
        // this compatibility assembly does not bind to Enchantment.dll.
        MethodInfo getSlotCount = FindMethod(table.GetType(), "GetSlotCount", 1);
        MethodInfo getSlotValue = FindMethod(table.GetType(), "GetSlotValue", 1);
        if (getSlotCount == null || getSlotValue == null) {
            return false;
        }

        try {
            if (getSlotCount.Invoke(table, [2]) is not int resultCount || resultCount != 1) {
                return false;
            }
            if (getSlotValue.Invoke(table, [2]) is not int resultValue || resultValue == 0) {
                return false;
            }

            if (getSlotCount.Invoke(table, [0]) is not int itemCount || itemCount <= 0
                || getSlotCount.Invoke(table, [1]) is not int bookCount || bookCount <= 0) {
                return false;
            }

            // The server-side ComponentEnchantingTable owns the actual
            // combination result. Requiring both input slots and a non-empty
            // result avoids turning this request into a generic inventory
            // removal primitive.
            return HasExpectedResultShape(resultValue);
        }
        catch {
            return false;
        }
    }

    private static bool HasExpectedResultShape(int resultValue) {
        // The result is an item value; a non-zero value is the strongest
        // version-neutral check available without linking to the mod's block
        // classes. The original RemoveSlotItems method performs the final
        // semantic check and remains authoritative.
        return resultValue != 0;
    }

    private static MethodInfo FindRemoveSlotItemsMethod(Type type) {
        for (Type current = type; current != null; current = current.BaseType) {
            MethodInfo method = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate =>
                    candidate.Name == RemoveSlotItemsMethodName
                    && !candidate.IsStatic
                    && candidate.ReturnType == typeof(int)
                    && candidate.GetParameters().Length == 2
                    && candidate.GetParameters().All(parameter => parameter.ParameterType == typeof(int)));
            if (method != null) {
                return method;
            }
        }

        return null;
    }

    private static Type ResolveLoadedType(string fullName) {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            Type type = assembly.GetType(fullName, throwOnError: false);
            if (type != null) {
                return type;
            }
        }

        return null;
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
