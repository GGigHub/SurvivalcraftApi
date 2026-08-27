using System.Collections;
using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Runtime bridge for SushiTool's global channel inventories. SushiTool stores
/// these inventories in a subsystem rather than in an entity component, so the
/// normal entity/inventory packets cannot fully identify or refresh them.
/// </summary>
public static class SushiToolCompatNetRuntime {
    public const string SyncBoxTypeName = "Sushi.ComponentSushiSyncBox";
    public const string SyncBoxSubsystemTypeName = "Sushi.SubsystemSushiSyncBox";
    public const string SyncInventoryTypeName = "Sushi.SushiSyncInventory";
    public const string TargetAssemblyName = "SushiTool.dll";
    private const string SushiMachineTypeName = "Sushi.SushiMachine";
    private const string SushiTrainTypeName = "Sushi.ComponentSushiTrain";

    private const int MaximumChannelIndex = 62;
    private const float MachineInteractionDistanceSquared = 64f;
    private const double PublishInterval = 0.25;
    private const double FullRefreshInterval = 5.0;
    private const BindingFlags InstanceFieldFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool s_forceFullRefresh;
    private static bool s_replayingTrainCall;

    public static void Reset() {
        s_forceFullRefresh = false;
        s_replayingTrainCall = false;
    }

    /// <summary>
    /// Forwards the two stateful controls on SushiMachineWidget to the server.
    /// SushiTool updates the machine locally in the widget, while CompatNet
    /// keeps the remote machine tick server-authoritative.
    /// </summary>
    public static void NotifyMachineWidgetAction(object widget) {
        if (widget == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return;
        }

        ComponentPlayer player = GetFieldValue(widget, "m_componentPlayer") as ComponentPlayer;
        Component machine = GetFieldValue(widget, "m_machine") as Component;
        if (player?.PlayerData?.IsMainPlayer != true
            || machine?.Entity == null
            || !IsSushiMachine(machine)) {
            return;
        }

        if (IsClicked(GetFieldValue(widget, "m_OverStop"))) {
            bool current = GetFieldValue(machine, "m_currentOverStop") is bool value && value;
            QueueMachineControl(
                player,
                machine,
                SushiToolMachineControlKind.OverStop,
                !current);
        }

        if (IsClicked(GetFieldValue(widget, "m_Start"))) {
            bool current = GetFieldValue(machine, "m_startWork") is bool value && value;
            QueueMachineControl(
                player,
                machine,
                SushiToolMachineControlKind.StartWork,
                !current);
        }
    }

    /// <summary>
    /// Sends a completed SushiMachineWorkConfig before the original dialog
    /// applies its optimistic local copy. The server reconstructs the
    /// version-specific config through the machine's own factory.
    /// </summary>
    public static void NotifyMachineDialogClose(
        object dialog,
        bool saveConfig,
        bool startWork) {
        if (dialog == null
            || !saveConfig
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return;
        }

        ComponentPlayer player = GetFieldValue(dialog, "m_componentPlayer") as ComponentPlayer;
        Component machine = GetFieldValue(dialog, "m_machine") as Component;
        object config = GetFieldValue(dialog, "m_workConfig");
        if (player?.PlayerData?.IsMainPlayer != true
            || machine?.Entity == null
            || !IsSushiMachine(machine)
            || config == null) {
            return;
        }

        MethodInfo serialize = config.GetType().GetMethod(
            "SerializeWorkConfig",
            InstanceFieldFlags,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        string configXml;
        try {
            configXml = serialize?.Invoke(config, null) as string;
        }
        catch {
            return;
        }

        if (string.IsNullOrWhiteSpace(configXml)
            || configXml.Length > SushiToolCompatNetPayloads.MaximumMachineConfigLength) {
            return;
        }

        QueueMachineConfigRequest(player, machine, configXml, startWork);
    }

    /// <summary>
    /// SushiFarmerMachineSettingDialog has its own settings class and does not
    /// pass through SushiMachineDialog.CloseDialog. Capture its pending
    /// values before the dialog applies them locally.
    /// </summary>
    public static void NotifyFarmerMachineDialogApply(object dialog) {
        if (dialog == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return;
        }

        ComponentPlayer player = GetFieldValue(dialog, "m_componentPlayer") as ComponentPlayer;
        Component machine = GetFieldValue(dialog, "m_componentBuilder") as Component;
        if (player?.PlayerData?.IsMainPlayer != true
            || machine?.Entity == null
            || !IsSushiMachine(machine)) {
            return;
        }

        object config;
        try {
            MethodInfo export = machine.GetType().GetMethod(
                "ExportConfig",
                InstanceFieldFlags,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            config = export?.Invoke(machine, null);
        }
        catch {
            return;
        }

        if (config == null) {
            return;
        }

        // These are the values held by the farmer-specific dialog before its
        // ApplySettings method writes them to the machine component.
        SetFieldValue(config, "OnlyTake", GetFieldValue(dialog, "m_onlyTake"));
        SetFieldValue(config, "AllPick", GetFieldValue(dialog, "m_allPick"));
        SetFieldValue(config, "AutoSeedConvert", GetFieldValue(dialog, "m_autoSeedConvert"));
        SetFieldValue(config, "NeedFertilize", GetFieldValue(dialog, "m_needFertilize"));
        SetFieldValue(config, "SeedRetainCount", GetFieldValue(dialog, "m_seedRetainCount"));

        string configXml;
        try {
            MethodInfo serialize = config.GetType().GetMethod(
                "SerializeWorkConfig",
                InstanceFieldFlags,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            configXml = serialize?.Invoke(config, null) as string;
        }
        catch {
            return;
        }

        if (string.IsNullOrWhiteSpace(configXml)
            || configXml.Length > SushiToolCompatNetPayloads.MaximumMachineConfigLength) {
            return;
        }

        QueueMachineConfigRequest(player, machine, configXml, startWork: false);
    }

    /// <summary>
    /// Relays the edge-triggered train speed/turn input. The client keeps the
    /// local rider responsive, while the server applies the same deltas to the
    /// authoritative train state.
    /// </summary>
    public static void NotifyTrainUpdate(object train) {
        if (train is not Component component
            || component.Entity?.Id <= 0
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || !TryGetTrainRider(train, out ComponentPlayer player)
            || player.PlayerData?.IsMainPlayer != true
            || player.ComponentInput == null) {
            return;
        }

        PlayerInput input = player.ComponentInput.PlayerInput;
        int speedDelta = 0;
        if (IsTrainInputReady(train, "m_canClickGo") && input.Move.Z != 0f) {
            speedDelta = input.Move.Z > 0f ? 1 : -1;
        }

        int turnDelta = 0;
        if (IsTrainInputReady(train, "m_canClickTurn") && input.Move.X != 0f) {
            turnDelta = input.Move.X > 0f ? 1 : -1;
        }

        if (speedDelta == 0 && turnDelta == 0) {
            return;
        }

        CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.TrainControlRequest,
            SushiToolCompatNetPayloads.WriteTrainControl(
                new SushiToolTrainControlRequest {
                    TrainEntityId = component.Entity.Id,
                    SpeedDelta = speedDelta,
                    TurnDelta = turnDelta
                }));
    }

    /// <summary>
    /// Prevents a client-side train recycle from creating a duplicate pickable.
    /// The server invokes the original TrainRecycle method after authenticating
    /// the mounted player.
    /// </summary>
    public static bool NotifyTrainRecycle(object train) {
        if (train is not Component component
            || component.Entity == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || !TryGetTrainRider(train, out ComponentPlayer player)
            || player.PlayerData?.IsMainPlayer != true) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.TrainRecycleRequest,
            SushiToolCompatNetPayloads.WriteTrainRecycle(
                new SushiToolTrainRecycleRequest {
                    TrainEntityId = component.Entity.Id
                }));
    }

    /// <summary>
    /// Relays the train call action so clients whose remote train update is
    /// suppressed still hear the same call sound and receive the noise event.
    /// The replay flag lets the broadcast invoke the original patched method
    /// without turning it back into a request.
    /// </summary>
    public static bool NotifyTrainCall(object train) {
        if (s_replayingTrainCall
            || train is not Component component
            || component.Entity?.Id <= 0
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || !TryGetTrainRider(train, out ComponentPlayer player)
            || player.PlayerData?.IsMainPlayer != true) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.TrainCallRequest,
            SushiToolCompatNetPayloads.WriteTrainCall(
                new SushiToolTrainCallRequest {
                    TrainEntityId = component.Entity.Id
                }));
    }

    /// <summary>
    /// Applies a server-authoritative train call event on a client. The target
    /// method remains owned by SushiTool, so audio/noise behavior stays
    /// version-specific instead of being duplicated in CompatNet.
    /// </summary>
    public static void HandleBroadcast(CompatNetPacket packet) {
        if (packet?.Operation != (ushort)SushiToolCompatNetOperation.TrainCallEvent
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return;
        }

        SushiToolTrainCallRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadTrainCall(packet.Payload);
        }
        catch {
            return;
        }

        Component train = FindSushiTrain(
            GameManager.Project?.FindEntity(request.TrainEntityId));
        if (train == null) {
            return;
        }

        MethodInfo call = train.GetType().GetMethod(
            "TrainCall",
            InstanceFieldFlags,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (call == null) {
            return;
        }

        try {
            s_replayingTrainCall = true;
            call.Invoke(train, null);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to replay SushiTool train call: {e}");
        }
        finally {
            s_replayingTrainCall = false;
        }
    }

    /// <summary>
    /// Keeps the local player's current train update alive. Other SushiTool
    /// trains remain covered by the all-non-player update guard.
    /// </summary>
    public static bool IsLocallyRiddenTrain(IUpdateable updateable) {
        return updateable is Component component
            && IsSushiTrain(component)
            && TryGetTrainRider(component, out ComponentPlayer player)
            && player.PlayerData?.IsMainPlayer == true;
    }

    public static bool QueueAddChannelRequest(
        Subsystem subsystem,
        ComponentPlayer player) {
        if (subsystem == null
            || player?.PlayerData == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.AddChannelRequest,
            []);
    }

    public static bool TryQueueUpdateChannelName(
        Subsystem subsystem,
        int channelIndex,
        string channelName,
        Color fontColor) {
        if (subsystem == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || channelIndex < 0
            || channelIndex > MaximumChannelIndex) {
            return false;
        }

        ComponentPlayer player = FindMainPlayer();
        if (player?.PlayerData == null) {
            return false;
        }

        CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.ChannelMetadataRequest,
            SushiToolCompatNetPayloads.WriteMetadata(new SushiToolChannelMetadataRequest {
                Kind = SushiToolChannelMetadataKind.UpdateName,
                ChannelIndex = channelIndex,
                ChannelName = channelName ?? string.Empty,
                ColorR = fontColor.R,
                ColorG = fontColor.G,
                ColorB = fontColor.B
            }));
        // Let SushiTool continue its original local mutation so the dialog
        // remains responsive. The server snapshot is authoritative afterward.
        return false;
    }

    public static bool TryQueueReorderChannel(
        Subsystem subsystem,
        int sourceOrderIndex,
        int targetOrderIndex) {
        if (subsystem == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || sourceOrderIndex < 0
            || targetOrderIndex < 0
            || sourceOrderIndex > MaximumChannelIndex
            || targetOrderIndex > MaximumChannelIndex) {
            return false;
        }

        ComponentPlayer player = FindMainPlayer();
        if (player?.PlayerData == null) {
            return false;
        }

        CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.ChannelMetadataRequest,
            SushiToolCompatNetPayloads.WriteMetadata(new SushiToolChannelMetadataRequest {
                Kind = SushiToolChannelMetadataKind.Reorder,
                SourceOrderIndex = sourceOrderIndex,
                TargetOrderIndex = targetOrderIndex
            }));
        // ReorderChannelOrder must still run locally; its return value drives
        // the dialog refresh. The server later publishes the authoritative
        // order to every client.
        return false;
    }

    public static bool TryQueueMoveItem(
        IInventory sourceInventory,
        int sourceSlotIndex,
        IInventory targetInventory,
        int targetSlotIndex,
        int count) {
        return TryQueueInventoryAction(
            new SushiToolInventoryActionRequest {
                Kind = SushiToolInventoryActionKind.Move,
                SourceSlot = sourceSlotIndex,
                TargetSlot = targetSlotIndex,
                Count = count
            },
            sourceInventory,
            targetInventory,
            out _);
    }

    public static bool TryQueueDragDropItem(
        IInventory sourceInventory,
        int sourceSlotIndex,
        DragMode dragMode,
        IInventory targetInventory,
        int targetSlotIndex,
        bool processingOnly,
        int dragCount) {
        return TryQueueInventoryAction(
            new SushiToolInventoryActionRequest {
                Kind = SushiToolInventoryActionKind.DragDrop,
                SourceSlot = sourceSlotIndex,
                TargetSlot = targetSlotIndex,
                DragMode = dragMode,
                ProcessingOnly = processingOnly,
                Count = dragCount
            },
            sourceInventory,
            targetInventory,
            out _);
    }

    public static bool TryQueuePlayerDrop(
        PlayerData playerData,
        int inventoryId,
        int slotIndex,
        Vector3 hitPosition,
        int count) {
        if (playerData?.ComponentPlayer == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return false;
        }

        IInventory inventory = GameManager.Project?
            .FindSubsystem<SubsystemInventories>()?
            .GetInventoryById(inventoryId);
        if (!TryGetSharedChannelIndex(inventory, out int channelIndex)) {
            return false;
        }

        SushiToolInventoryActionRequest request = new() {
            Kind = SushiToolInventoryActionKind.Drop,
            SourceChannel = channelIndex,
            SourceSlot = slotIndex,
            Count = count,
            HitPosition = hitPosition
        };
        return CompatNetCore.QueueRequest(
            playerData.ComponentPlayer,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.InventoryActionRequest,
            SushiToolCompatNetPayloads.WriteAction(request));
    }

    public static void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (packet == null
            || playerData?.ComponentPlayer == null
            || !NetworkManager.IsServerRunning) {
            return;
        }

        if (packet.Operation == (ushort)SushiToolCompatNetOperation.MachineControlRequest) {
            HandleMachineControlRequest(packet.Payload, playerData);
            return;
        }
        if (packet.Operation == (ushort)SushiToolCompatNetOperation.MachineConfigRequest) {
            HandleMachineConfigRequest(packet.Payload, playerData);
            return;
        }
        if (packet.Operation == (ushort)SushiToolCompatNetOperation.TrainControlRequest) {
            HandleTrainControlRequest(packet.Payload, playerData);
            return;
        }
        if (packet.Operation == (ushort)SushiToolCompatNetOperation.TrainRecycleRequest) {
            HandleTrainRecycleRequest(packet.Payload, playerData);
            return;
        }
        if (packet.Operation == (ushort)SushiToolCompatNetOperation.TrainCallRequest) {
            HandleTrainCallRequest(packet.Payload, playerData);
            return;
        }
        if (packet.Operation == (ushort)SushiToolCompatNetOperation.InventoryActionRequest) {
            HandleInventoryActionRequest(packet.Payload, playerData);
            return;
        }
        if (packet.Operation == (ushort)SushiToolCompatNetOperation.ChannelMetadataRequest) {
            HandleChannelMetadataRequest(packet.Payload, playerData);
            return;
        }
        if (packet.Operation != (ushort)SushiToolCompatNetOperation.AddChannelRequest) {
            return;
        }

        object subsystem = FindSyncBoxSubsystem();
        if (subsystem == null) {
            return;
        }

        MethodInfo addChannel = subsystem.GetType().GetMethod(
            "AddChannel",
            InstanceFieldFlags,
            binder: null,
            types: [typeof(ComponentPlayer)],
            modifiers: null);
        if (addChannel == null) {
            return;
        }

        try {
            object result = addChannel.Invoke(subsystem, [playerData.ComponentPlayer]);
            if (result is int channelIndex && channelIndex >= 0) {
                s_forceFullRefresh = true;
            }
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to add SushiTool channel: {e}");
        }
    }

    private static void HandleMachineControlRequest(
        byte[] payload,
        PlayerData playerData) {
        SushiToolMachineControlRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadMachineControl(payload);
        }
        catch {
            return;
        }

        ComponentPlayer player = playerData.ComponentPlayer;
        Entity entity = GameManager.Project?.FindEntity(request.MachineEntityId);
        Component machine = FindSushiMachine(entity);
        if (machine == null || !IsWithinMachineRange(player, machine)) {
            return;
        }

        string fieldName = request.Kind == SushiToolMachineControlKind.StartWork
            ? "m_startWork"
            : "m_currentOverStop";
        if (SetBoolField(machine, fieldName, request.Enabled)) {
            // The client already flipped the button optimistically. The next
            // scalar snapshot should repair rejected/stale local state too.
            s_forceFullRefresh = true;
        }
    }

    private static void HandleMachineConfigRequest(
        byte[] payload,
        PlayerData playerData) {
        SushiToolMachineConfigRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadMachineConfig(payload);
        }
        catch {
            return;
        }

        ComponentPlayer player = playerData.ComponentPlayer;
        Component machine = FindSushiMachine(
            GameManager.Project?.FindEntity(request.MachineEntityId));
        if (machine == null || !IsWithinMachineRange(player, machine)) {
            return;
        }

        try {
            MethodInfo createConfig = machine.GetType().GetMethod(
                "CreateWorkConfig",
                InstanceFieldFlags,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            object config = createConfig?.Invoke(machine, null);
            if (config == null) {
                return;
            }

            MethodInfo extractConfig = config.GetType().GetMethod(
                "ExtractWorkConfig",
                InstanceFieldFlags,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            if (extractConfig?.Invoke(config, [request.ConfigXml]) is not bool extracted
                || !extracted) {
                return;
            }

            MethodInfo pasteConfig = machine.GetType().GetMethods(InstanceFieldFlags)
                .FirstOrDefault(method =>
                    method.Name == "PasteConfig"
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType.FullName
                        == "Sushi.SushiMachineWorkConfig");
            if (pasteConfig == null) {
                return;
            }

            pasteConfig.Invoke(machine, [config]);
            if (request.StartWork) {
                SetFieldValue(machine, "m_infoText", string.Empty);
                SetBoolField(machine, "m_startWork", true);
            }
            s_forceFullRefresh = true;
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to apply SushiTool machine config: {e}");
        }
    }

    private static void HandleTrainControlRequest(
        byte[] payload,
        PlayerData playerData) {
        SushiToolTrainControlRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadTrainControl(payload);
        }
        catch {
            return;
        }

        ComponentPlayer player = playerData.ComponentPlayer;
        Component train = FindSushiTrain(
            GameManager.Project?.FindEntity(request.TrainEntityId));
        if (train == null || !IsPlayerRidingTrain(player, train)) {
            return;
        }

        ApplyTrainControl(train, request);
    }

    private static void HandleTrainRecycleRequest(
        byte[] payload,
        PlayerData playerData) {
        SushiToolTrainRecycleRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadTrainRecycle(payload);
        }
        catch {
            return;
        }

        ComponentPlayer player = playerData.ComponentPlayer;
        Component train = FindSushiTrain(
            GameManager.Project?.FindEntity(request.TrainEntityId));
        if (train == null || !IsPlayerRidingTrain(player, train)) {
            return;
        }

        MethodInfo recycle = train.GetType().GetMethod(
            "TrainRecycle",
            InstanceFieldFlags,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        try {
            recycle?.Invoke(train, null);
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to recycle SushiTool train: {e}");
        }
    }

    private static void HandleTrainCallRequest(
        byte[] payload,
        PlayerData playerData) {
        SushiToolTrainCallRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadTrainCall(payload);
        }
        catch {
            return;
        }

        ComponentPlayer player = playerData.ComponentPlayer;
        Component train = FindSushiTrain(
            GameManager.Project?.FindEntity(request.TrainEntityId));
        if (train == null || !IsPlayerRidingTrain(player, train)) {
            return;
        }

        MethodInfo call = train.GetType().GetMethod(
            "TrainCall",
            InstanceFieldFlags,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (call == null) {
            return;
        }

        try {
            call.Invoke(train, null);
            CompatNetCore.QueueBroadcast(
                ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
                (ushort)SushiToolCompatNetOperation.TrainCallEvent,
                SushiToolCompatNetPayloads.WriteTrainCall(request));
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to process SushiTool train call: {e}");
        }
    }

    private static void ApplyTrainControl(
        Component train,
        SushiToolTrainControlRequest request) {
        int currentSpeedIndex = GetIntField(train, "m_speedIndex") ?? 0;
        Array speedEnum = GetStaticFieldValue(train.GetType(), "SpeedEnum") as Array;
        int maximumSpeedIndex = speedEnum?.Length > 0 ? speedEnum.Length - 1 : 6;
        int nextSpeedIndex = Math.Clamp(
            currentSpeedIndex + request.SpeedDelta,
            0,
            maximumSpeedIndex);
        if (!TrySetIntField(train, "m_speedIndex", nextSpeedIndex)) {
            return;
        }

        if (speedEnum?.GetValue(nextSpeedIndex) is int speed) {
            SetFieldValue(train, "m_speed", (float)speed);
        }

        if (request.TurnDelta != 0) {
            int currentTurn = GetIntField(train, "m_nextTurn") ?? 0;
            int nextTurn = currentTurn != request.TurnDelta
                ? request.TurnDelta
                : 0;
            TrySetIntField(train, "m_nextTurn", nextTurn);
        }
    }

    private static Component FindSushiTrain(Entity entity) {
        return entity?.Components.FirstOrDefault(component => IsSushiTrain(component));
    }

    private static bool IsSushiTrain(Component component) {
        return component != null
            && string.Equals(
                component.GetType().FullName,
                SushiTrainTypeName,
                StringComparison.Ordinal)
            && NamesEqual(
                component.GetType().Assembly.GetName().Name,
                TargetAssemblyName);
    }

    private static bool IsPlayerRidingTrain(
        ComponentPlayer player,
        Component train) {
        return player?.Entity != null
            && TryGetTrainRider(train, out ComponentPlayer rider)
            && rider.Entity == player.Entity;
    }

    private static bool TryGetTrainRider(
        object train,
        out ComponentPlayer player) {
        player = null;
        try {
            ComponentMount mount = GetFieldValue(train, "m_componentMount") as ComponentMount;
            ComponentRider rider = mount?.Rider;
            player = rider?.Entity?.FindComponent<ComponentPlayer>();
            return player != null;
        }
        catch {
            return false;
        }
    }

    private static bool IsTrainInputReady(object train, string fieldName) {
        return GetFieldValue(train, fieldName) is not bool ready || ready;
    }

    private static void QueueMachineControl(
        ComponentPlayer player,
        Component machine,
        SushiToolMachineControlKind kind,
        bool enabled) {
        CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.MachineControlRequest,
            SushiToolCompatNetPayloads.WriteMachineControl(
                new SushiToolMachineControlRequest {
                    MachineEntityId = machine.Entity.Id,
                    Kind = kind,
                    Enabled = enabled
                }));
    }

    private static void QueueMachineConfigRequest(
        ComponentPlayer player,
        Component machine,
        string configXml,
        bool startWork) {
        CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.MachineConfigRequest,
            SushiToolCompatNetPayloads.WriteMachineConfig(
                new SushiToolMachineConfigRequest {
                    MachineEntityId = machine.Entity.Id,
                    StartWork = startWork,
                    ConfigXml = configXml
                }));
    }

    private static bool IsClicked(object button) {
        if (button == null) {
            return false;
        }

        try {
            PropertyInfo property = button.GetType().GetProperty(
                "IsClicked",
                InstanceFieldFlags);
            return property?.GetValue(button) is bool clicked && clicked;
        }
        catch {
            return false;
        }
    }

    private static Component FindSushiMachine(Entity entity) {
        return entity?.Components.FirstOrDefault(IsSushiMachine);
    }

    private static bool IsSushiMachine(Component component) {
        if (component == null
            || !NamesEqual(component.GetType().Assembly.GetName().Name, TargetAssemblyName)) {
            return false;
        }

        for (Type current = component.GetType();
             current != null && current != typeof(object);
             current = current.BaseType) {
            if (string.Equals(
                    current.FullName,
                    SushiMachineTypeName,
                    StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    private static bool IsWithinMachineRange(
        ComponentPlayer player,
        Component machine) {
        ComponentFrame playerFrame = player?.Entity?.FindComponent<ComponentFrame>();
        ComponentBlockEntity blockEntity = machine?.Entity?.FindComponent<ComponentBlockEntity>();
        return playerFrame != null
            && blockEntity != null
            && Vector3.DistanceSquared(playerFrame.Position, blockEntity.Position)
                <= MachineInteractionDistanceSquared;
    }

    private static bool SetBoolField(
        object instance,
        string name,
        bool value) {
        FieldInfo field = FindField(instance?.GetType(), name);
        if (field?.FieldType != typeof(bool)) {
            return false;
        }

        try {
            field.SetValue(instance, value);
            return true;
        }
        catch {
            return false;
        }
    }

    private static void HandleChannelMetadataRequest(
        byte[] payload,
        PlayerData playerData) {
        SushiToolChannelMetadataRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadMetadata(payload);
        }
        catch {
            return;
        }

        // The channel dialog updates the local UI optimistically. A full
        // channel snapshot will restore it if validation or invocation fails.
        s_forceFullRefresh = true;
        object subsystem = FindSyncBoxSubsystem();
        if (subsystem == null) {
            return;
        }

        try {
            switch (request.Kind) {
                case SushiToolChannelMetadataKind.UpdateName:
                    if (request.ChannelIndex < 0
                        || request.ChannelIndex > MaximumChannelIndex
                        || !TryGetChannelDictionary(
                            subsystem,
                            out IDictionary inventories)
                        || !inventories.Contains(request.ChannelIndex)) {
                        return;
                    }

                    MethodInfo updateName = subsystem.GetType().GetMethod(
                        "UpdateChannelName",
                        InstanceFieldFlags,
                        binder: null,
                        types: [typeof(int), typeof(string), typeof(Color)],
                        modifiers: null);
                    updateName?.Invoke(
                        subsystem,
                        [
                            request.ChannelIndex,
                            request.ChannelName ?? string.Empty,
                            new Color(request.ColorR, request.ColorG, request.ColorB)
                        ]);
                    break;
                case SushiToolChannelMetadataKind.Reorder:
                    if (request.SourceOrderIndex < 0
                        || request.TargetOrderIndex < 0
                        || request.SourceOrderIndex > MaximumChannelIndex
                        || request.TargetOrderIndex > MaximumChannelIndex) {
                        return;
                    }

                    MethodInfo reorder = subsystem.GetType().GetMethod(
                        "ReorderChannelOrder",
                        InstanceFieldFlags,
                        binder: null,
                        types: [typeof(int), typeof(int)],
                        modifiers: null);
                    reorder?.Invoke(
                        subsystem,
                        [request.SourceOrderIndex, request.TargetOrderIndex]);
                    break;
            }
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to apply SushiTool channel metadata: {e}");
        }
    }

    private static void HandleInventoryActionRequest(
        byte[] payload,
        PlayerData playerData) {
        SushiToolInventoryActionRequest request;
        try {
            request = SushiToolCompatNetPayloads.ReadAction(payload);
        }
        catch {
            return;
        }

        // InventorySlotWidget performs an optimistic local mutation before
        // the server receives this request. Force a channel snapshot even if
        // validation rejects the action so the client cannot retain it.
        s_forceFullRefresh = true;

        ComponentPlayer player = playerData.ComponentPlayer;
        IInventory playerInventory = player.ComponentMiner?.Inventory;
        IInventory sourceInventory = null;
        IInventory targetInventory = null;
        if (!TryResolveActionInventory(
                playerInventory,
                request.SourceChannel,
                out sourceInventory)
            || !TryResolveActionInventory(
                playerInventory,
                request.TargetChannel,
                out targetInventory)) {
            QueuePlayerInventoryCorrection(
                request.SourceChannel < 0 ? sourceInventory : null,
                request.TargetChannel < 0 ? targetInventory : null);
            return;
        }

        switch (request.Kind) {
            case SushiToolInventoryActionKind.Move:
                ApplyMove(
                    sourceInventory,
                    request.SourceSlot,
                    targetInventory,
                    request.TargetSlot,
                    request.Count);
                break;
            case SushiToolInventoryActionKind.DragDrop:
                ApplyDragDrop(
                    sourceInventory,
                    request.SourceSlot,
                    request.DragMode,
                    targetInventory,
                    request.TargetSlot,
                    request.ProcessingOnly,
                    request.Count);
                break;
            case SushiToolInventoryActionKind.Drop:
                if (request.SourceChannel >= 0
                    && request.TargetChannel < 0
                    && InventoryPacketHelpers.IsSlotIndexValid(
                        sourceInventory,
                        request.SourceSlot)
                    && request.Count > 0) {
                    int count = Math.Min(
                        request.Count,
                        sourceInventory.GetSlotCount(request.SourceSlot));
                    if (count > 0) {
                        player.ViewWidget?.DragDrop(
                            request.HitPosition,
                            new InventoryDragData {
                                Inventory = sourceInventory,
                                SlotIndex = request.SourceSlot,
                                Count = count
                            },
                            count,
                            fromNetwork: true);
                    }
                }
                break;
        }

        QueuePlayerInventoryCorrection(
            request.SourceChannel < 0 ? sourceInventory : null,
            request.TargetChannel < 0 ? targetInventory : null);
    }

    private static void ApplyMove(
        IInventory sourceInventory,
        int sourceSlotIndex,
        IInventory targetInventory,
        int targetSlotIndex,
        int count) {
        if (count <= 0
            || !InventoryPacketHelpers.IsSlotIndexValid(sourceInventory, sourceSlotIndex)
            || !InventoryPacketHelpers.IsSlotIndexValid(targetInventory, targetSlotIndex)) {
            return;
        }

        int value = sourceInventory.GetSlotValue(sourceSlotIndex);
        int targetValue = targetInventory.GetSlotValue(targetSlotIndex);
        int sourceCount = sourceInventory.GetSlotCount(sourceSlotIndex);
        int targetCount = targetInventory.GetSlotCount(targetSlotIndex);
        int targetCapacity = targetInventory.GetSlotCapacity(targetSlotIndex, value);
        int actualCount = targetCount == 0 || value == targetValue
            ? MathUtils.Min(targetCapacity - targetCount, sourceCount, count)
            : 0;
        if (actualCount > 0) {
            int removedCount = sourceInventory.RemoveSlotItems(sourceSlotIndex, actualCount);
            if (removedCount > 0) {
                targetInventory.AddSlotItems(targetSlotIndex, value, removedCount);
            }
        }
    }

    private static void ApplyDragDrop(
        IInventory sourceInventory,
        int sourceSlotIndex,
        DragMode dragMode,
        IInventory targetInventory,
        int targetSlotIndex,
        bool processingOnly,
        int requestedDragCount) {
        if (requestedDragCount <= 0
            || !InventoryPacketHelpers.IsSlotIndexValid(sourceInventory, sourceSlotIndex)
            || !InventoryPacketHelpers.IsSlotIndexValid(targetInventory, targetSlotIndex)) {
            return;
        }

        int sourceSlotValue = sourceInventory.GetSlotValue(sourceSlotIndex);
        int targetSlotValue = targetInventory.GetSlotValue(targetSlotIndex);
        int sourceSlotCount = sourceInventory.GetSlotCount(sourceSlotIndex);
        int targetSlotCount = targetInventory.GetSlotCount(targetSlotIndex);
        int targetSlotCapacity = targetInventory.GetSlotCapacity(targetSlotIndex, sourceSlotValue);
        int targetSlotProcessCapacity = targetInventory.GetSlotProcessCapacity(
            targetSlotIndex,
            sourceSlotValue);
        int actualDragCount = MathUtils.Min(requestedDragCount, sourceSlotCount);

        if (targetSlotProcessCapacity > 0) {
            int processCount = sourceInventory.RemoveSlotItems(
                sourceSlotIndex,
                MathUtils.Min(actualDragCount, targetSlotProcessCapacity));
            targetInventory.ProcessSlotItems(
                sourceInventory,
                sourceSlotIndex,
                targetSlotIndex,
                sourceSlotValue,
                actualDragCount,
                processCount,
                out int processedValue,
                out int processedCount);
            if (processedValue != 0 && processedCount != 0) {
                int count = MathUtils.Min(
                    sourceInventory.GetSlotCapacity(sourceSlotIndex, processedValue),
                    processedCount);
                sourceInventory.RemoveSlotItems(sourceSlotIndex, count);
                sourceInventory.AddSlotItems(sourceSlotIndex, processedValue, count);
            }
        }
        else if (!processingOnly) {
            if ((targetSlotCount == 0 || sourceSlotValue == targetSlotValue)
                && targetSlotCount < targetSlotCapacity) {
                int moveCount = MathUtils.Min(
                    targetSlotCapacity - targetSlotCount,
                    actualDragCount);
                int removedCount = sourceInventory.RemoveSlotItems(sourceSlotIndex, moveCount);
                targetInventory.AddSlotItems(targetSlotIndex, sourceSlotValue, removedCount);
            }
            else if (targetInventory.GetSlotCapacity(targetSlotIndex, sourceSlotValue) >= actualDragCount
                && sourceInventory.GetSlotCapacity(sourceSlotIndex, targetSlotValue) >= targetSlotCount
                && sourceSlotCount == actualDragCount) {
                int removedTargetCount = targetInventory.RemoveSlotItems(
                    targetSlotIndex,
                    targetSlotCount);
                int removedSourceCount = sourceInventory.RemoveSlotItems(
                    sourceSlotIndex,
                    actualDragCount);
                targetInventory.AddSlotItems(
                    targetSlotIndex,
                    sourceSlotValue,
                    removedSourceCount);
                sourceInventory.AddSlotItems(
                    sourceSlotIndex,
                    targetSlotValue,
                    removedTargetCount);
            }
        }
    }

    private static void QueuePlayerInventoryCorrection(
        IInventory sourceInventory,
        IInventory targetInventory) {
        if (sourceInventory != null) {
            InventoryPacketHelpers.QueueInventorySync(sourceInventory);
        }
        if (targetInventory != null && !ReferenceEquals(sourceInventory, targetInventory)) {
            InventoryPacketHelpers.QueueInventorySync(targetInventory);
        }
    }

    private static bool TryQueueInventoryAction(
        SushiToolInventoryActionRequest request,
        IInventory sourceInventory,
        IInventory targetInventory,
        out ComponentPlayer player) {
        player = FindMainPlayer();
        if (player == null
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || !TryGetActionEndpoint(sourceInventory, out request.SourceChannel)
            || !TryGetActionEndpoint(targetInventory, out request.TargetChannel)
            || request.SourceChannel < 0 && request.TargetChannel < 0) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.InventoryActionRequest,
            SushiToolCompatNetPayloads.WriteAction(request));
    }

    private static bool TryResolveActionInventory(
        IInventory playerInventory,
        int channelIndex,
        out IInventory inventory) {
        if (channelIndex == -1) {
            inventory = playerInventory;
            return inventory != null;
        }
        if (channelIndex < 0 || channelIndex > MaximumChannelIndex) {
            inventory = null;
            return false;
        }

        object subsystem = FindSyncBoxSubsystem();
        if (subsystem == null
            || !TryGetChannelDictionary(subsystem, out IDictionary inventories)
            || !inventories.Contains(channelIndex)) {
            inventory = null;
            return false;
        }

        inventory = inventories[channelIndex] as IInventory;
        return inventory != null;
    }

    private static bool TryGetActionEndpoint(
        IInventory inventory,
        out int channelIndex) {
        channelIndex = -1;
        if (inventory == null) {
            return false;
        }

        ComponentPlayer mainPlayer = FindMainPlayer();
        if (ReferenceEquals(inventory, mainPlayer?.ComponentMiner?.Inventory)) {
            return true;
        }

        if (inventory is Component component
            && component.GetType().FullName == SyncBoxTypeName) {
            object subsystem = GetFieldValue(component, "m_subsystemSushiSyncBox");
            int? componentChannel = GetIntField(component, "channelIndex");
            if (componentChannel.HasValue
                && componentChannel.Value >= 0
                && componentChannel.Value <= MaximumChannelIndex
                && subsystem != null
                && TryGetChannelDictionary(subsystem, out IDictionary inventories)
                && inventories.Contains(componentChannel.Value)) {
                channelIndex = componentChannel.Value;
                return true;
            }
            return false;
        }

        return TryGetSharedChannelIndex(inventory, out channelIndex);
    }

    private static bool TryGetSharedChannelIndex(
        IInventory inventory,
        out int channelIndex) {
        channelIndex = -1;
        if (inventory == null
            || inventory.GetType().FullName != SyncInventoryTypeName) {
            return false;
        }

        object subsystem = FindSyncBoxSubsystem();
        if (subsystem == null
            || !TryGetChannelDictionary(subsystem, out IDictionary inventories)) {
            return false;
        }

        foreach (DictionaryEntry entry in inventories) {
            if (entry.Key is int candidate
                && candidate >= 0
                && candidate <= MaximumChannelIndex
                && ReferenceEquals(entry.Value, inventory)) {
                channelIndex = candidate;
                return true;
            }
        }
        return false;
    }

    private static ComponentPlayer FindMainPlayer() {
        return GameManager.Project?
            .FindSubsystem<SubsystemPlayers>()?
            .MainPlayer;
    }

    public static void Publish(
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        ref int sequence,
        Dictionary<int, byte[]> lastPayloads) {
        if (!NetworkManager.IsServerRunning || GameManager.Project == null) {
            return;
        }

        double now = Time.RealTime;
        if (now < nextPublishTime) {
            return;
        }
        nextPublishTime = now + PublishInterval;

        bool fullRefresh = s_forceFullRefresh || now >= nextFullRefreshTime;
        if (fullRefresh) {
            nextFullRefreshTime = now + FullRefreshInterval;
        }

        object subsystem = FindSyncBoxSubsystem();
        if (subsystem == null
            || !TryGetChannelDictionary(subsystem, out IDictionary inventories)) {
            return;
        }

        List<SushiToolChannelState> changed = [];
        foreach (DictionaryEntry entry in inventories) {
            if (entry.Key is not int channelIndex
                || channelIndex < 0
                || channelIndex > MaximumChannelIndex
                || !TryCaptureChannel(
                    subsystem,
                    entry.Value,
                    channelIndex,
                    out SushiToolChannelState state)) {
                continue;
            }

            byte[] statePayload = SushiToolCompatNetPayloads.Write(
                new SushiToolChannelStateBatch {
                    Sequence = 0,
                    FullRefresh = false,
                    Channels = [state]
                });
            if (fullRefresh
                || !lastPayloads.TryGetValue(channelIndex, out byte[] previous)
                || !previous.AsSpan().SequenceEqual(statePayload)) {
                lastPayloads[channelIndex] = statePayload;
                changed.Add(state);
                if (changed.Count >= SushiToolCompatNetPayloads.MaximumChannelsPerBatch) {
                    break;
                }
            }
        }

        s_forceFullRefresh = false;
        if (changed.Count == 0) {
            return;
        }

        sequence = NextSequence(sequence);
        QueueBatches(changed, sequence, fullRefresh);
    }

    public static void Apply(byte[] payload, ref int lastAppliedSequence) {
        if (payload == null
            || payload.Length == 0
            || payload.Length > CompatNetPacket.MaximumPayloadBytes) {
            return;
        }

        SushiToolChannelStateBatch batch = SushiToolCompatNetPayloads.Read(payload);
        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))) {
            return;
        }
        object subsystem = FindSyncBoxSubsystem();
        if (subsystem == null
            || !TryGetChannelDictionary(subsystem, out IDictionary inventories)) {
            return;
        }

        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        if (batch.FullRefresh) {
            RemoveMissingChannels(inventories, batch.Channels);
        }

        foreach (SushiToolChannelState channel in batch.Channels) {
            if (channel == null
                || channel.ChannelIndex < 0
                || channel.ChannelIndex > MaximumChannelIndex
                || !TryGetOrCreateInventory(
                    subsystem,
                    inventories,
                    channel.Slots.Count,
                    channel.ChannelIndex,
                    out object inventory)) {
                continue;
            }

            ApplyChannel(subsystem, inventory, channel);
        }
    }

    /// <summary>
    /// Implements ComponentSushiSyncBox.SetSlotValue without referencing the
    /// optional SushiTool assembly at compile time.
    /// </summary>
    public static void SetSlotValueBridge(
        Component component,
        int slotIndex,
        object value) {
        if (component == null
            || value is not ComponentInventoryBase.Slot sourceSlot
            || !TryGetInventoryFromComponent(component, out object inventory)) {
            return;
        }

        try {
            if (!TryGetSlots(inventory, out IList slots)
                || slotIndex < 0
                || slotIndex >= slots.Count
                || slots[slotIndex] == null) {
                return;
            }

            object targetSlot = slots[slotIndex];
            int normalizedValue = sourceSlot.Value;
            int count = sourceSlot.Count;
            NormalizeSlot(ref normalizedValue, ref count);
            SetIntField(targetSlot, "Value", normalizedValue);
            SetIntField(targetSlot, "Count", count);
            NotifyInventoryMutation(component, slotIndex);
        }
        catch {
            // Optional third-party object layouts are versioned independently.
            // A failed correction must not break the network receive path.
        }
    }

    /// <summary>
    /// Implements SushiSyncInventory.SetSlotValue for the inventory instance
    /// used by the held synchronization box. This inventory is not a
    /// Component, so the entity-component bridge above cannot be reused.
    /// </summary>
    public static void SetSharedSlotValueBridge(
        object inventory,
        int slotIndex,
        object value) {
        if (inventory is not IInventory sharedInventory
            || value is not ComponentInventoryBase.Slot sourceSlot) {
            return;
        }

        try {
            if (!TrySetInventorySlotValue(
                    sharedInventory,
                    slotIndex,
                    sourceSlot.Value,
                    sourceSlot.Count)) {
                return;
            }
            NotifySharedInventoryMutation(sharedInventory, slotIndex);
        }
        catch {
            // Optional third-party object layouts are versioned independently.
            // A failed correction must not break the network receive path.
        }
    }

    /// <summary>
    /// Marks a custom inventory component dirty for the standard inventory
    /// packet path. The SushiTool override bypasses ComponentInventoryBase's
    /// OnSlotChange implementation, so this notification must be explicit.
    /// </summary>
    public static void NotifyInventoryMutation(Component component, int slotIndex) {
        if (!NetworkManager.IsServerRunning || component is not IInventory inventory) {
            return;
        }

        // ComponentSushiSyncBox is only a proxy; its base ComponentInventoryBase
        // slots are not the authoritative storage. Do not enqueue a standard
        // component packet with that unused slot list.
        if (component.GetType().FullName == SyncBoxTypeName
            && TryGetInventoryFromComponent(component, out object shared)
            && shared is IInventory sharedInventory) {
            NotifySharedInventoryMutation(sharedInventory, slotIndex);
            return;
        }

        InventoryPacketHelpers.MarkInventoryDirty(inventory);
    }

    /// <summary>
    /// Requests a channel snapshot for SushiTool's non-component shared
    /// inventory. It must not enter the ordinary inventory-packet queue:
    /// the shared inventory ID is process-local and could collide with an
    /// unrelated client inventory.
    /// </summary>
    public static void NotifySharedInventoryMutation(
        IInventory inventory,
        int slotIndex) {
        if (NetworkManager.IsServerRunning) {
            s_forceFullRefresh = true;
        }
    }

    private static void QueueBatches(
        List<SushiToolChannelState> channels,
        int sequence,
        bool fullRefresh) {
        List<SushiToolChannelState> current = [];
        foreach (SushiToolChannelState channel in channels) {
            current.Add(channel);
            byte[] payload = SushiToolCompatNetPayloads.Write(new SushiToolChannelStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Channels = current
            });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                QueueBatch(current, sequence, fullRefresh);
            }

            current = [channel];
        }

        if (current.Count > 0) {
            byte[] payload = SushiToolCompatNetPayloads.Write(new SushiToolChannelStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Channels = current
            });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                QueueBatch(current, sequence, fullRefresh);
            }
        }
    }

    private static void QueueBatch(
        List<SushiToolChannelState> channels,
        int sequence,
        bool fullRefresh) {
        CompatNetCore.QueueBroadcast(
            ScopedUpdateGuardCompatNetAdapter.SushiToolAdapterId,
            (ushort)SushiToolCompatNetOperation.ChannelStateSnapshot,
            SushiToolCompatNetPayloads.Write(new SushiToolChannelStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Channels = channels
            }));
    }

    private static void RemoveMissingChannels(
        IDictionary inventories,
        IEnumerable<SushiToolChannelState> channels) {
        HashSet<int> present = channels
            .Where(channel => channel != null)
            .Select(channel => channel.ChannelIndex)
            .ToHashSet();
        List<int> stale = [];
        foreach (DictionaryEntry entry in inventories) {
            if (entry.Key is int channelIndex
                && channelIndex >= 0
                && channelIndex <= MaximumChannelIndex
                && !present.Contains(channelIndex)) {
                stale.Add(channelIndex);
            }
        }
        foreach (int channelIndex in stale) {
            inventories.Remove(channelIndex);
        }

        object subsystem = FindSyncBoxSubsystem();
        if (subsystem == null
            || !TryGetChannelOrderDictionary(subsystem, out IDictionary order)) {
            return;
        }

        List<int> staleOrderIndices = [];
        foreach (DictionaryEntry entry in order) {
            if (entry.Key is int orderIndex
                && entry.Value is int channelIndex
                && !present.Contains(channelIndex)) {
                staleOrderIndices.Add(orderIndex);
            }
        }
        foreach (int orderIndex in staleOrderIndices) {
            order.Remove(orderIndex);
        }
    }

    private static bool TryCaptureChannel(
        object subsystem,
        object inventory,
        int channelIndex,
        out SushiToolChannelState state) {
        state = new SushiToolChannelState { ChannelIndex = channelIndex };
        if (!TryGetSlots(inventory, out IList slots)
            || slots.Count > SushiToolCompatNetPayloads.MaximumSlotsPerChannel) {
            return false;
        }

        for (int i = 0; i < slots.Count; i++) {
            object slot = slots[i];
            if (slot == null
                || !TryGetIntField(slot, "Value", out int value)
                || !TryGetIntField(slot, "Count", out int count)) {
                return false;
            }

            if (value == 0 || count <= 0) {
                value = 0;
                count = 0;
            }
            state.Slots.Add(new SushiToolChannelSlotState {
                Value = value,
                Count = count
            });
        }

        CaptureChannelMetadata(subsystem, channelIndex, state);

        return true;
    }

    private static void ApplyChannel(
        object subsystem,
        object inventory,
        SushiToolChannelState state) {
        ApplyChannelMetadata(subsystem, state);
        if (!TryGetSlots(inventory, out IList slots)) {
            return;
        }

        if (slots.Count != state.Slots.Count) {
            // The slot count is part of the world/mod configuration. Refuse
            // a partial update if the two sides disagree, otherwise a stale
            // tail could remain in the shared channel inventory.
            return;
        }

        int count = slots.Count;
        for (int i = 0; i < count; i++) {
            object targetSlot = slots[i];
            SushiToolChannelSlotState sourceSlot = state.Slots[i];
            if (targetSlot == null || sourceSlot == null) {
                continue;
            }

            int value = sourceSlot.Value;
            int slotCount = sourceSlot.Count;
            NormalizeSlot(ref value, ref slotCount);
            SetIntField(targetSlot, "Value", value);
            SetIntField(targetSlot, "Count", slotCount);
        }
    }

    private static void CaptureChannelMetadata(
        object subsystem,
        int channelIndex,
        SushiToolChannelState state) {
        if (TryGetChannelNameDictionary(subsystem, out IDictionary names)
            && names.Contains(channelIndex)) {
            object font = names[channelIndex];
            string name = GetFieldValue(font, "Text") as string;
            if (!string.IsNullOrEmpty(name)) {
                state.ChannelName = name.Length > SushiToolCompatNetPayloads.MaximumChannelNameLength
                    ? name[..SushiToolCompatNetPayloads.MaximumChannelNameLength]
                    : name;
            }

            if (GetFieldValue(font, "Color") is Color color) {
                state.ColorR = color.R;
                state.ColorG = color.G;
                state.ColorB = color.B;
            }
        }

        if (TryGetChannelOrderDictionary(subsystem, out IDictionary order)) {
            foreach (DictionaryEntry entry in order) {
                if (entry.Key is int orderIndex
                    && entry.Value is int orderedChannel
                    && orderedChannel == channelIndex) {
                    state.OrderIndex = orderIndex;
                    break;
                }
            }
        }
    }

    private static void ApplyChannelMetadata(
        object subsystem,
        SushiToolChannelState state) {
        if (TryGetChannelNameDictionary(subsystem, out IDictionary names)) {
            object font = names.Contains(state.ChannelIndex)
                ? names[state.ChannelIndex]
                : null;
            if (font == null) {
                font = CreateChannelFont(state.ChannelName, state);
            }
            if (font != null) {
                SetFieldValue(font, "Text", state.ChannelName ?? string.Empty);
                SetFieldValue(
                    font,
                    "Color",
                    new Color(state.ColorR, state.ColorG, state.ColorB));
                names[state.ChannelIndex] = font;
            }
        }

        if (state.OrderIndex >= 0
            && TryGetChannelOrderDictionary(subsystem, out IDictionary order)) {
            order[state.OrderIndex] = state.ChannelIndex;
        }
    }

    private static object CreateChannelFont(
        string channelName,
        SushiToolChannelState state) {
        Type fontType = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            fontType = assembly.GetType("Sushi.SushiFont", false);
            if (fontType != null) {
                break;
            }
        }
        ConstructorInfo constructor = fontType?.GetConstructor(
            InstanceFieldFlags,
            binder: null,
            types: [typeof(string), typeof(Color)],
            modifiers: null);
        if (constructor == null) {
            return null;
        }

        try {
            return constructor.Invoke([
                channelName ?? string.Empty,
                new Color(state.ColorR, state.ColorG, state.ColorB)
            ]);
        }
        catch {
            return null;
        }
    }

    private static bool TryGetOrCreateInventory(
        object subsystem,
        IDictionary inventories,
        int requestedSlotCount,
        int channelIndex,
        out object inventory) {
        inventory = inventories.Contains(channelIndex)
            ? inventories[channelIndex]
            : null;
        if (inventory != null) {
            return true;
        }

        Type inventoryType = FindInventoryType(inventories);
        int slotCount = GetIntField(subsystem, "OneBoxSlotCount") ?? requestedSlotCount;
        if (inventoryType == null
            || slotCount <= 0
            || slotCount > SushiToolCompatNetPayloads.MaximumSlotsPerChannel) {
            return false;
        }

        ConstructorInfo constructor = inventoryType.GetConstructor(
            InstanceFieldFlags,
            binder: null,
            types: [typeof(Project), typeof(ValuesDictionary), typeof(int)],
            modifiers: null);
        if (constructor == null) {
            return false;
        }

        try {
            inventory = constructor.Invoke([GameManager.Project, null, slotCount]);
            inventories[channelIndex] = inventory;
            MethodInfo ensureChannelOrder = subsystem.GetType().GetMethod(
                "EnsureChannelOrder",
                InstanceFieldFlags,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            ensureChannelOrder?.Invoke(subsystem, null);
            return inventory != null;
        }
        catch {
            inventory = null;
            return false;
        }
    }

    private static Type FindInventoryType(IDictionary inventories) {
        foreach (DictionaryEntry entry in inventories) {
            if (entry.Value != null) {
                return entry.Value.GetType();
            }
        }
        return FindTargetAssembly()?.GetType(SyncInventoryTypeName, false);
    }

    private static object FindSyncBoxSubsystem() {
        Type type = FindTargetAssembly()?.GetType(SyncBoxSubsystemTypeName, false);
        return type == null
            ? null
            : GameManager.Project?.FindSubsystem(type, null, false);
    }

    private static Assembly FindTargetAssembly() {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!NamesEqual(assembly.GetName().Name, TargetAssemblyName)
                && !NamesEqual(assembly.ManifestModule?.Name, TargetAssemblyName)) {
                continue;
            }

            if (assembly.GetType(SyncBoxSubsystemTypeName, false) != null) {
                return assembly;
            }
        }
        return null;
    }

    private static bool TryGetChannelDictionary(
        object subsystem,
        out IDictionary inventories) {
        inventories = GetFieldValue(subsystem, "SushiSyncInventories") as IDictionary;
        return inventories != null;
    }

    private static bool TryGetChannelNameDictionary(
        object subsystem,
        out IDictionary names) {
        names = GetFieldValue(subsystem, "m_channelNames") as IDictionary;
        return names != null;
    }

    private static bool TryGetChannelOrderDictionary(
        object subsystem,
        out IDictionary order) {
        order = GetFieldValue(subsystem, "ChannelOrder") as IDictionary;
        return order != null;
    }

    private static bool TryGetInventoryFromComponent(
        Component component,
        out object inventory) {
        inventory = null;
        object subsystem = GetFieldValue(component, "m_subsystemSushiSyncBox");
        int? channelIndex = GetIntField(component, "channelIndex");
        return subsystem != null
            && channelIndex.HasValue
            && TryGetChannelDictionary(subsystem, out IDictionary inventories)
            && channelIndex.Value >= 0
            && channelIndex.Value <= MaximumChannelIndex
            && inventories.Contains(channelIndex.Value)
            && inventories[channelIndex.Value] is object found
            && (inventory = found) != null;
    }

    private static bool TryGetSlots(object inventory, out IList slots) {
        slots = GetFieldValue(inventory, "m_slots") as IList;
        return slots != null;
    }

    private static bool TrySetInventorySlotValue(
        IInventory inventory,
        int slotIndex,
        int value,
        int count) {
        if (!TryGetSlots(inventory, out IList slots)
            || slotIndex < 0
            || slotIndex >= slots.Count
            || slots[slotIndex] == null) {
            return false;
        }

        NormalizeSlot(ref value, ref count);
        object targetSlot = slots[slotIndex];
        SetIntField(targetSlot, "Value", value);
        SetIntField(targetSlot, "Count", count);
        return true;
    }

    private static object GetFieldValue(object instance, string name) {
        if (instance == null) {
            return null;
        }

        try {
            return FindField(instance.GetType(), name)?.GetValue(instance);
        }
        catch {
            return null;
        }
    }

    private static void SetIntField(object instance, string name, int value) {
        TrySetIntField(instance, name, value);
    }

    private static bool TrySetIntField(object instance, string name, int value) {
        FieldInfo field = FindField(instance?.GetType(), name);
        if (field?.FieldType != typeof(int)) {
            return false;
        }

        try {
            field.SetValue(instance, value);
            return true;
        }
        catch {
            return false;
        }
    }

    private static void SetFieldValue(
        object instance,
        string name,
        object value) {
        FieldInfo field = FindField(instance?.GetType(), name);
        if (field != null && (value == null || field.FieldType.IsInstanceOfType(value))) {
            try {
                field.SetValue(instance, value);
            }
            catch {
            }
        }
    }

    private static object GetStaticFieldValue(Type type, string name) {
        for (Type current = type;
             current != null && current != typeof(object);
             current = current.BaseType) {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);
            if (field == null) {
                continue;
            }

            try {
                return field.GetValue(null);
            }
            catch {
                return null;
            }
        }
        return null;
    }

    private static bool TryGetIntField(object instance, string name, out int value) {
        value = 0;
        FieldInfo field = FindField(instance?.GetType(), name);
        if (field?.FieldType != typeof(int)) {
            return false;
        }
        object raw = field.GetValue(instance);
        if (raw is not int intValue) {
            return false;
        }
        value = intValue;
        return true;
    }

    private static int? GetIntField(object instance, string name) {
        return TryGetIntField(instance, name, out int value) ? value : null;
    }

    private static void NormalizeSlot(ref int value, ref int count) {
        if (value == 0 || count <= 0) {
            value = 0;
            count = 0;
        }
    }

    private static FieldInfo FindField(Type type, string name) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            FieldInfo field = current.GetField(name, InstanceFieldFlags | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        return null;
    }

    private static bool NamesEqual(string left, string right) {
        return string.Equals(
            NormalizeName(left),
            NormalizeName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string name) {
        if (string.IsNullOrEmpty(name)) {
            return string.Empty;
        }
        string value = name.Replace('\\', '/');
        int separator = value.LastIndexOf('/');
        if (separator >= 0) {
            value = value[(separator + 1)..];
        }
        return value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static int NextSequence(int sequence) {
        return sequence == int.MaxValue ? 1 : sequence + 1;
    }

    private static bool IsSequenceNewer(int sequence, int previous) {
        if (previous == 0) {
            return sequence > 0;
        }
        if (sequence == previous) {
            return false;
        }
        if (previous == int.MaxValue) {
            return sequence == 1;
        }
        if (previous < int.MaxValue / 2 && sequence > int.MaxValue / 2) {
            return false;
        }
        return sequence > previous;
    }

    private static bool IsSequenceAtLeast(int sequence, int previous) {
        return sequence == previous || IsSequenceNewer(sequence, previous);
    }
}
