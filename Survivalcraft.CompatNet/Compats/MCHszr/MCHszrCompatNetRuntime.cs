using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Runtime side of the MCHszr compatibility bridge. The target assembly is
/// never referenced statically; all target members are resolved by exact
/// metadata names after the optional mod has loaded.
/// </summary>
public static class MCHszrCompatNetRuntime {
    private const string TargetAssemblySimpleName = "MCHszr";
    private const string TargetLoaderTypeName = "MCHszr.UnifiedMCHszrModLoader";
    private const string TargetPoseBehaviorTypeName = "MCHszr.SubsystemSpearEditBehavior";
    private const string TargetFoodBehaviorTypeName = "MCHszr.SubsystemMCEdibleItemBehavior";

    private const string SpearBlockTypeName = "SpearBlock";
    private const string HammerBlockTypeName = "HammerBlock";

    private const float SpearMinimumChargeSeconds = 0.5f;
    private const float SpearMaximumChargeSeconds = 3f;
    private const float HammerMinimumChargeSeconds = 0.4f;
    private const float HammerMaximumChargeSeconds = 2f;

    private static readonly object s_stateLock = new();
    private static readonly Dictionary<ComponentPlayer, int> s_clientSequences = [];
    private static readonly Dictionary<ComponentPlayer, int> s_serverSequences = [];
    private static readonly Dictionary<ComponentPlayer, ClientChargeState> s_clientCharges = [];
    private static readonly Dictionary<ComponentPlayer, ServerChargeState> s_serverCharges = [];

    private sealed class ClientChargeState {
        public MCHszrCompatNetAction Action;
        public int EntityId;
        public int SlotIndex;
        public int ItemValue;
    }

    private sealed class ServerChargeState {
        public MCHszrCompatNetAction Action;
        public int EntityId;
        public int SlotIndex;
        public int ItemValue;
        public double StartedAt;
    }

    /// <summary>
    /// The existing remote-player update guard remains deliberately narrow:
    /// only MCHszr components on non-main player entities are skipped on a
    /// client. Server, listen-server, local-player, and global logic run.
    /// </summary>
    public static bool ShouldSkipClientUpdate(IUpdateable updateable) {
        if (!NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || updateable is not Component component
            || component.Entity == null
            || !IsMCHszrAssembly(updateable.GetType().Assembly)) {
            return false;
        }

        ComponentPlayer player = component.Entity.FindComponent<ComponentPlayer>();
        return player?.PlayerData != null && !player.PlayerData.IsMainPlayer;
    }

    /// <summary>
    /// Called from the Cecil guard at the beginning of
    /// UnifiedMCHszrModLoader.UpdatePlayerInputAim. On a client, combat-pose
    /// spear/hammer input is converted to a begin/release intent and the
    /// original local special-action path is skipped. Other input continues to
    /// the original mod unchanged.
    /// </summary>
    public static bool TryHandlePlayerInput(
        object loader,
        ComponentPlayer player,
        bool aiming,
        ref bool playerOperated,
        out bool skipVanilla) {
        skipVanilla = false;

        if (!IsClientOnly()
            || player?.PlayerData?.IsMainPlayer != true
            || player.Entity == null
            || !TryGetActiveAction(player, requireCombatPose: true, out MCHszrCompatNetAction action, out int slotIndex, out int itemValue)) {
            return false;
        }

        if (aiming) {
            lock (s_stateLock) {
                if (s_clientCharges.TryGetValue(player, out ClientChargeState state)) {
                    if (state.Action != action || state.SlotIndex != slotIndex) {
                        s_clientCharges.Remove(player);
                    }
                    else {
                        playerOperated = true;
                        skipVanilla = true;
                        return true;
                    }
                }

                MCHszrCompatNetRequest request = NewRequest(
                    player,
                    action,
                    slotIndex,
                    itemValue);
                if (!CompatNetCore.QueueRequest(
                        player,
                        MCHszrCompatNetAdapter.StableAdapterId,
                        (ushort)MCHszrCompatNetOperation.ChargeBegin,
                        CompatNetPayload.Write(request.Serialize))) {
                    return false;
                }

                s_clientCharges[player] = new ClientChargeState {
                    Action = action,
                    EntityId = player.Entity.Id,
                    SlotIndex = slotIndex,
                    ItemValue = itemValue
                };
            }

            playerOperated = true;
            skipVanilla = true;
            return true;
        }

        ClientChargeState charge;
        lock (s_stateLock) {
            if (!s_clientCharges.TryGetValue(player, out charge)
                || charge.Action != action
                || charge.SlotIndex != slotIndex) {
                return false;
            }

            s_clientCharges.Remove(player);
            MCHszrCompatNetRequest request = NewRequest(
                player,
                charge.Action,
                charge.SlotIndex,
                charge.ItemValue);
            if (!CompatNetCore.QueueRequest(
                    player,
                    MCHszrCompatNetAdapter.StableAdapterId,
                    (ushort)MCHszrCompatNetOperation.ChargeRelease,
                    CompatNetPayload.Write(request.Serialize))) {
                return false;
            }
        }

        playerOperated = true;
        skipVanilla = true;
        return true;
    }

    /// <summary>
    /// Called from SubsystemSpearEditBehavior.OnEditInventoryItem. It queues
    /// the authenticated player's pose toggle but always returns false so the
    /// original client-side UI and pose state still update immediately.
    /// </summary>
    public static bool TryQueuePoseToggle(
        object behavior,
        IInventory inventory,
        int slotIndex,
        ComponentPlayer player) {
        if (!IsClientOnly()
            || player?.PlayerData?.IsMainPlayer != true
            || inventory == null
            || player.Entity == null
            || slotIndex < 0
            || slotIndex >= inventory.SlotsCount
            || !TryGetWeaponAction(inventory.GetSlotValue(slotIndex), out _)) {
            return false;
        }

        MCHszrCompatNetRequest request = NewRequest(
            player,
            default,
            slotIndex,
            inventory.GetSlotValue(slotIndex));
        CompatNetCore.QueueRequest(
            player,
            MCHszrCompatNetAdapter.StableAdapterId,
            (ushort)MCHszrCompatNetOperation.PoseToggle,
            CompatNetPayload.Write(request.Serialize));

        // The Cecil patch deliberately discards this value. Returning false
        // documents that the target's local toggle must not be suppressed.
        return false;
    }

    /// <summary>
    /// Called from SubsystemMCEdibleItemBehavior.ConsumeItemAndEat. The
    /// client sends only its current slot and item value; the server rechecks
    /// the actual inventory, nutrition, hunger, satiation, and target cooldown.
    /// </summary>
    public static bool TryHandleFoodConsume(
        object behavior,
        ComponentMiner miner,
        int itemValue) {
        if (!IsClientOnly()
            || miner?.ComponentPlayer?.PlayerData?.IsMainPlayer != true
            || miner.ComponentPlayer.Entity == null
            || !TryValidateFoodLocally(behavior, miner, itemValue, out int slotIndex)) {
            return false;
        }

        ComponentPlayer player = miner.ComponentPlayer;
        MCHszrCompatNetRequest request = NewRequest(
            player,
            default,
            slotIndex,
            itemValue);
        return CompatNetCore.QueueRequest(
            player,
            MCHszrCompatNetAdapter.StableAdapterId,
            (ushort)MCHszrCompatNetOperation.FoodConsume,
            CompatNetPayload.Write(request.Serialize));
    }

    public static bool TryTogglePose(
        PlayerData playerData,
        MCHszrCompatNetRequest request) {
        if (!TryResolvePlayer(playerData, request, out ComponentPlayer player)
            || player.ComponentMiner?.Inventory == null
            || request.SlotIndex != player.ComponentMiner.Inventory.ActiveSlotIndex
            || !TryGetInventoryItem(player, request.SlotIndex, out int actualValue, out _)
            || !SameContents(actualValue, request.ItemValue)
            || !TryGetWeaponAction(actualValue, out _)) {
            return false;
        }

        if (!TryAcceptServerSequence(player, request.Sequence)) {
            return false;
        }

        object poseBehavior = FindSubsystem(player.Project, TargetPoseBehaviorTypeName);
        if (poseBehavior == null) {
            return false;
        }

        MethodInfo method = FindMethod(
            poseBehavior.GetType(),
            "OnEditInventoryItem",
            parameterCount: 3);
        if (method == null) {
            return false;
        }

        return InvokeMethod(method, poseBehavior, [
            player.ComponentMiner.Inventory,
            request.SlotIndex,
            player
        ], out _);
    }

    public static bool TryBeginCharge(
        PlayerData playerData,
        MCHszrCompatNetRequest request) {
        if (!IsSupportedAction(request.Action)
            || !TryResolvePlayer(playerData, request, out ComponentPlayer player)
            || !TryGetActiveAction(player, requireCombatPose: true, out MCHszrCompatNetAction actualAction, out int slotIndex, out int itemValue)
            || actualAction != request.Action
            || slotIndex != request.SlotIndex
            || !SameContents(itemValue, request.ItemValue)
            || HasMount(player)) {
            return false;
        }

        if (!TryAcceptServerSequence(player, request.Sequence)) {
            return false;
        }

        lock (s_stateLock) {
            // The original loader owns one private charge/dash state globally.
            // Serialize accepted charges so two players cannot overwrite it.
            if (s_serverCharges.Count != 0) {
                return false;
            }

            s_serverCharges[player] = new ServerChargeState {
                Action = request.Action,
                EntityId = request.EntityId,
                SlotIndex = slotIndex,
                ItemValue = itemValue,
                StartedAt = Time.FrameStartTime
            };
        }
        return true;
    }

    public static bool TryReleaseCharge(
        PlayerData playerData,
        MCHszrCompatNetRequest request) {
        if (!IsSupportedAction(request.Action)
            || !TryResolvePlayer(playerData, request, out ComponentPlayer player)
            || !TryGetActiveAction(player, requireCombatPose: true, out MCHszrCompatNetAction actualAction, out int slotIndex, out int itemValue)
            || actualAction != request.Action
            || slotIndex != request.SlotIndex
            || !SameContents(itemValue, request.ItemValue)
            || HasMount(player)) {
            return false;
        }

        if (!TryAcceptServerSequence(player, request.Sequence)) {
            return false;
        }

        ServerChargeState charge;
        lock (s_stateLock) {
            if (!s_serverCharges.TryGetValue(player, out charge)
                || charge.Action != request.Action
                || charge.EntityId != request.EntityId
                || charge.SlotIndex != slotIndex) {
                return false;
            }
            s_serverCharges.Remove(player);
        }

        double elapsed = Time.FrameStartTime - charge.StartedAt;
        if (!IsChargeDurationAllowed(charge.Action, elapsed)) {
            return false;
        }

        return InvokeServerAction(player, charge.Action, elapsed);
    }

    public static bool TryConsumeFood(
        PlayerData playerData,
        MCHszrCompatNetRequest request) {
        if (request.Action != default
            || !TryResolvePlayer(playerData, request, out ComponentPlayer player)
            || player.ComponentMiner?.Inventory == null
            || request.SlotIndex != player.ComponentMiner.Inventory.ActiveSlotIndex
            || !TryGetInventoryItem(player, request.SlotIndex, out int actualValue, out int count)
            || count <= 0
            || !SameContents(actualValue, request.ItemValue)) {
            return false;
        }

        if (!TryValidateFoodServer(player, request.ItemValue, out object behavior)) {
            return false;
        }

        if (!TryAcceptServerSequence(player, request.Sequence)) {
            return false;
        }

        MethodInfo method = FindMethod(
            behavior.GetType(),
            "ConsumeItemAndEat",
            parameterCount: 2);
        return method != null && InvokeVoid(method, behavior, [
            player.ComponentMiner,
            actualValue
        ]);
    }

    /// <summary>
    /// Useful for focused tests and keeps the source-derived charge limits in
    /// one place. The input is server-measured time, never a client value.
    /// </summary>
    public static bool IsChargeDurationAllowed(
        MCHszrCompatNetAction action,
        double elapsedSeconds) {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)) {
            return false;
        }

        return action switch {
            MCHszrCompatNetAction.SpearDash =>
                elapsedSeconds >= SpearMinimumChargeSeconds
                && elapsedSeconds <= SpearMaximumChargeSeconds,
            MCHszrCompatNetAction.HammerShockwave =>
                elapsedSeconds >= HammerMinimumChargeSeconds
                && elapsedSeconds <= HammerMaximumChargeSeconds,
            _ => false
        };
    }

    private static bool TryValidateFoodLocally(
        object behavior,
        ComponentMiner miner,
        int itemValue,
        out int slotIndex) {
        slotIndex = -1;
        if (miner.Inventory == null) {
            return false;
        }

        slotIndex = miner.Inventory.ActiveSlotIndex;
        if (!TryGetInventoryItem(miner.ComponentPlayer, slotIndex, out int actualValue, out int count)
            || count <= 0
            || !SameContents(actualValue, itemValue)) {
            return false;
        }

        ComponentVitalStats vitalStats = miner.ComponentPlayer.ComponentVitalStats;
        if (vitalStats == null || vitalStats.Food >= 0.99f) {
            return false;
        }

        Block block = BlocksManager.Blocks[Terrain.ExtractContents(actualValue)];
        if (block == null || block.GetNutritionalValue(actualValue) <= 0f) {
            return false;
        }

        vitalStats.m_satiation.TryGetValue(
            Terrain.ExtractContents(actualValue),
            out float satiation);
        if (satiation > 6f) {
            return false;
        }

        return !IsFoodCooldownActive(behavior);
    }

    private static bool TryValidateFoodServer(
        ComponentPlayer player,
        int itemValue,
        out object behavior) {
        behavior = FindSubsystem(player.Project, TargetFoodBehaviorTypeName);
        if (behavior == null || IsFoodCooldownActive(behavior)) {
            return false;
        }

        ComponentVitalStats vitalStats = player.ComponentVitalStats;
        if (vitalStats == null || vitalStats.Food >= 0.99f) {
            return false;
        }

        Block block = BlocksManager.Blocks[Terrain.ExtractContents(itemValue)];
        if (block == null || block.GetNutritionalValue(itemValue) <= 0f) {
            return false;
        }

        vitalStats.m_satiation.TryGetValue(
            Terrain.ExtractContents(itemValue),
            out float satiation);
        return satiation <= 6f;
    }

    private static bool IsFoodCooldownActive(object behavior) {
        FieldInfo field = FindField(behavior?.GetType(), "m_eatCooldownElapsed");
        if (field == null) {
            // The target source has this field. If a future version removes or
            // renames it, refusing the custom request is safer than bypassing
            // an unknown cooldown implementation.
            return true;
        }

        object value = field.GetValue(behavior);
        return value is not float elapsed || elapsed > 0f;
    }

    private static bool InvokeServerAction(
        ComponentPlayer player,
        MCHszrCompatNetAction action,
        double elapsed) {
        object loader = FindTargetLoader();
        if (loader == null) {
            return false;
        }

        string methodName = action == MCHszrCompatNetAction.SpearDash
            ? "PerformSpearDash"
            : "TriggerHammerShockwave";
        string chargeFieldName = action == MCHszrCompatNetAction.SpearDash
            ? "m_spearChargeStartTime"
            : "m_hammerChargeStartTime";

        if (!SetField(
                loader,
                chargeFieldName,
                Time.FrameStartTime - Math.Clamp(elapsed, 0d, 3d))) {
            return false;
        }

        if (action == MCHszrCompatNetAction.SpearDash
            && !SetField(loader, "m_lastFrameTime", Time.FrameStartTime)) {
            return false;
        }

        if (!InvokeVoid(
                FindMethod(loader.GetType(), methodName, parameterCount: 1),
                loader,
                [player])) {
            return false;
        }

        if (action == MCHszrCompatNetAction.SpearDash) {
            ScheduleServerDash(loader, player, 0);
        }
        return true;
    }

    private static void ScheduleServerDash(
        object loader,
        ComponentPlayer player,
        int frameCount) {
        if (frameCount >= 30) {
            return;
        }

        Time.QueueFrameIndexDelayedExecution(
            Time.FrameIndex + 1,
            () => {
                try {
                    if (!NetworkManager.IsServerRunning
                        || !ReferenceEquals(
                            GetField(loader, "m_dashingPlayer"),
                            player)) {
                        return;
                    }

                    MethodInfo update = FindMethod(
                        loader.GetType(),
                        "UpdateDashing",
                        parameterCount: 1);
                    if (!InvokeVoid(update, loader, [null])
                        || frameCount + 1 >= 30) {
                        return;
                    }

                    ScheduleServerDash(loader, player, frameCount + 1);
                }
                catch (Exception e) {
                    Log.Error(
                        $"[Survivalcraft.CompatNet] MCHszr spear dash tick failed: {e}");
                }
            });
    }

    private static bool TryResolvePlayer(
        PlayerData playerData,
        MCHszrCompatNetRequest request,
        out ComponentPlayer player) {
        player = playerData?.ComponentPlayer;
        return player?.Entity != null
            && request != null
            && request.EntityId > 0
            && player.Entity.Id == request.EntityId;
    }

    private static bool TryGetActiveAction(
        ComponentPlayer player,
        bool requireCombatPose,
        out MCHszrCompatNetAction action,
        out int slotIndex,
        out int itemValue) {
        action = default;
        slotIndex = -1;
        itemValue = 0;

        IInventory inventory = player?.ComponentMiner?.Inventory;
        if (inventory == null) {
            return false;
        }

        slotIndex = inventory.ActiveSlotIndex;
        if (!TryGetInventoryItem(player, slotIndex, out itemValue, out int count)
            || count <= 0
            || !TryGetWeaponAction(itemValue, out action)) {
            return false;
        }

        if (requireCombatPose) {
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(itemValue)];
            return block != null && HasCombatPose(player, block);
        }
        return true;
    }

    private static bool TryGetWeaponAction(
        int itemValue,
        out MCHszrCompatNetAction action) {
        action = default;
        if (itemValue == 0) {
            return false;
        }

        Block block = BlocksManager.Blocks[Terrain.ExtractContents(itemValue)];
        if (block == null) {
            return false;
        }

        Type type = block.GetType();
        if (!IsMCHszrAssembly(type.Assembly)) {
            return false;
        }

        if (string.Equals(type.Name, HammerBlockTypeName, StringComparison.Ordinal)) {
            action = MCHszrCompatNetAction.HammerShockwave;
            return true;
        }

        if (string.Equals(type.Name, SpearBlockTypeName, StringComparison.Ordinal)
            || IsSpearCategory(block)) {
            action = MCHszrCompatNetAction.SpearDash;
            return true;
        }

        return false;
    }

    private static bool IsSpearCategory(Block block) {
        Type type = FindTargetType("MCHszr.WeaponConfigModLoader");
        MethodInfo method = FindMethod(type, "IsSpearCategory", parameterCount: 1);
        if (method == null) {
            return false;
        }

        try {
            return method.Invoke(null, [block]) is bool result && result;
        }
        catch {
            return false;
        }
    }

    private static bool HasCombatPose(ComponentPlayer player, Block block) {
        object behavior = FindSubsystem(player.Project, TargetPoseBehaviorTypeName);
        MethodInfo method = FindMethod(
            behavior?.GetType(),
            "GetPlayerPoseState",
            parameterCount: 2);
        if (method == null) {
            return false;
        }

        try {
            object value = method.Invoke(behavior, [
                player.PlayerData.Name,
                block
            ]);
            return value is int pose && pose == 1;
        }
        catch {
            return false;
        }
    }

    private static bool TryGetInventoryItem(
        ComponentPlayer player,
        int slotIndex,
        out int value,
        out int count) {
        value = 0;
        count = 0;
        IInventory inventory = player?.ComponentMiner?.Inventory;
        if (inventory == null
            || slotIndex < 0
            || slotIndex >= inventory.SlotsCount) {
            return false;
        }

        value = inventory.GetSlotValue(slotIndex);
        count = inventory.GetSlotCount(slotIndex);
        return true;
    }

    private static bool HasMount(ComponentPlayer player) {
        return player?.ComponentRider?.Mount != null;
    }

    private static bool IsSupportedAction(MCHszrCompatNetAction action) {
        return action is MCHszrCompatNetAction.SpearDash
            or MCHszrCompatNetAction.HammerShockwave;
    }

    private static bool SameContents(int left, int right) {
        return left != 0
            && right != 0
            && Terrain.ExtractContents(left) == Terrain.ExtractContents(right);
    }

    private static MCHszrCompatNetRequest NewRequest(
        ComponentPlayer player,
        MCHszrCompatNetAction action,
        int slotIndex,
        int itemValue) {
        return new MCHszrCompatNetRequest {
            Action = action,
            EntityId = player.Entity.Id,
            SlotIndex = slotIndex,
            ItemValue = itemValue,
            Sequence = NextClientSequence(player)
        };
    }

    private static int NextClientSequence(ComponentPlayer player) {
        lock (s_stateLock) {
            if (!s_clientSequences.TryGetValue(player, out int sequence)
                || sequence >= int.MaxValue - 1) {
                sequence = 0;
            }

            sequence++;
            s_clientSequences[player] = sequence;
            return sequence;
        }
    }

    private static bool TryAcceptServerSequence(
        ComponentPlayer player,
        int sequence) {
        if (sequence <= 0) {
            return false;
        }

        lock (s_stateLock) {
            if (s_serverSequences.TryGetValue(player, out int previous)
                && sequence <= previous) {
                return false;
            }

            s_serverSequences[player] = sequence;
            return true;
        }
    }

    private static object FindTargetLoader() {
        return ModsManager.ModLoaders.FirstOrDefault(loader =>
            loader != null
            && string.Equals(
                loader.GetType().FullName,
                TargetLoaderTypeName,
                StringComparison.Ordinal)
            && IsMCHszrAssembly(loader.GetType().Assembly));
    }

    private static object FindSubsystem(
        Project project,
        string fullName) {
        return project?.Subsystems?.FirstOrDefault(subsystem =>
            subsystem != null
            && string.Equals(
                subsystem.GetType().FullName,
                fullName,
                StringComparison.Ordinal)
            && IsMCHszrAssembly(subsystem.GetType().Assembly));
    }

    private static Type FindTargetType(string fullName) {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => IsMCHszrAssembly(assembly))?
            .GetType(fullName, throwOnError: false);
    }

    private static MethodInfo FindMethod(
        Type type,
        string name,
        int parameterCount) {
        for (Type current = type; current != null; current = current.BaseType) {
            MethodInfo method = current.GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate =>
                    candidate.Name == name
                    && candidate.GetParameters().Length == parameterCount);
            if (method != null) {
                return method;
            }
        }
        return null;
    }

    private static FieldInfo FindField(Type type, string name) {
        for (Type current = type; current != null; current = current.BaseType) {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        return null;
    }

    private static object GetField(object target, string name) {
        return FindField(target?.GetType(), name)?.GetValue(target);
    }

    private static bool SetField(object target, string name, object value) {
        FieldInfo field = FindField(target?.GetType(), name);
        if (field == null || !field.FieldType.IsInstanceOfType(value)) {
            return false;
        }

        field.SetValue(target, value);
        return true;
    }

    private static bool InvokeVoid(
        MethodInfo method,
        object target,
        object[] parameters) {
        if (method == null || method.ReturnType != typeof(void)) {
            return false;
        }

        return InvokeMethod(method, target, parameters, out _);
    }

    private static bool InvokeMethod(
        MethodInfo method,
        object target,
        object[] parameters,
        out object result) {
        result = null;

        try {
            result = method.Invoke(target, parameters);
            return true;
        }
        catch (Exception e) {
            Log.Error(
                $"[Survivalcraft.CompatNet] MCHszr bridge invocation failed: {e}");
            return false;
        }
    }

    private static bool IsClientOnly() {
        return NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
    }

    private static bool IsMCHszrAssembly(Assembly assembly) {
        return assembly != null
            && string.Equals(
                assembly.GetName().Name,
                TargetAssemblySimpleName,
                StringComparison.OrdinalIgnoreCase);
    }
}
