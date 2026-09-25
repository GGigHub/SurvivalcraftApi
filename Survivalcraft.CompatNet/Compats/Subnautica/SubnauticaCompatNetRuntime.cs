using System.Collections;
using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Bridges Subnautica's mermaid interactions without taking a compile-time
/// dependency on the optional mod assembly. Requests are authenticated by the
/// multiplayer packet's sender and all mutations run on the server.
/// </summary>
public static class SubnauticaCompatNetRuntime {
    public const string TargetAssemblyName = "sc-subnautica-mod.dll";

    private const double PublishInterval = 0.5;
    private const double FullRefreshInterval = 5.0;
    private const float InteractionDistanceSquared = 36f;
    private const float KillRelayDistanceSquared = 4096f;
    private const string MermaidAffectionTypeName = "Subnautica.ComponentMermaidAffection";
    private const string MermaidQuestTypeName = "Subnautica.ComponentMermaidQuest";
    private const string AchievementManagerTypeName = "Subnautica.AchievementManager";
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool s_forceFullRefresh;
    private static readonly HashSet<int> s_serverHandledCreatureKills = [];

    public static void Reset() {
        s_forceFullRefresh = false;
        s_serverHandledCreatureKills.Clear();
    }

    /// <summary>
    /// Called by the patched Subnautica loader before it consumes a local fish
    /// interaction. Returning true makes the patched method exit, leaving the
    /// inventory and affection mutation to the server request handler.
    /// </summary>
    public static bool TryQueueMermaidFeed(
        ComponentPlayer player,
        ComponentCreature mermaid,
        Component affection) {
        if (!NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || player?.PlayerData == null
            || !player.PlayerData.IsMainPlayer
            || mermaid?.Entity == null
            || !IsType(affection, MermaidAffectionTypeName)) {
            return false;
        }

        IInventory inventory = player.ComponentMiner?.Inventory;
        if (inventory == null
            || inventory.ActiveSlotIndex < 0
            || inventory.ActiveSlotIndex >= inventory.SlotsCount
            || inventory.GetSlotCount(inventory.ActiveSlotIndex) <= 0
            || !TryGetFeedKind(inventory.GetSlotValue(inventory.ActiveSlotIndex), out SubnauticaMermaidFeedKind kind)) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
            (ushort)SubnauticaCompatNetOperation.MermaidFeedRequest,
            SubnauticaCompatNetPayloads.WriteFeedRequest(new SubnauticaMermaidFeedRequest {
                MermaidEntityId = mermaid.Entity.Id,
                Kind = kind
            }));
    }

    public static bool TryQueueMermaidGift(Component affection, int giftType) {
        if (!NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || !IsType(affection, MermaidAffectionTypeName)
            || giftType < 0
            || giftType > 3) {
            return false;
        }

        ComponentPlayer player = GetPropertyValue(
                FindProperty(affection.GetType(), "TargetPlayer"),
                affection) is Entity target
            ? target.FindComponent<ComponentPlayer>()
            : null;
        player ??= FindMainPlayer();
        if (player?.PlayerData?.IsMainPlayer != true
            || !CanInteract(player, affection.Entity)) {
            return false;
        }

        return CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
            (ushort)SubnauticaCompatNetOperation.MermaidGiftRequest,
            SubnauticaCompatNetPayloads.WriteGiftRequest(new SubnauticaMermaidGiftRequest {
                MermaidEntityId = affection.Entity.Id,
                GiftType = giftType
            }));
    }

    /// <summary>
    /// The original quest methods are allowed to update the local UI
    /// optimistically. This side effect sends the same intent to the server;
    /// the periodic snapshot corrects the client if validation rejects it.
    /// </summary>
    public static void QueueQuestAccept(Component questComponent, string questId) {
        if (!CanQueueQuestRequest(questComponent)
            || string.IsNullOrWhiteSpace(questId)
            || questId.Length > SubnauticaCompatNetPayloads.MaximumQuestIdLength) {
            return;
        }

        ComponentPlayer player = FindMainPlayer();
        if (player == null) {
            return;
        }

        CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
            (ushort)SubnauticaCompatNetOperation.MermaidQuestAcceptRequest,
            SubnauticaCompatNetPayloads.WriteQuestAcceptRequest(new SubnauticaMermaidQuestAcceptRequest {
                MermaidEntityId = questComponent.Entity.Id,
                QuestId = questId
            }));
    }

    public static void QueueQuestDelivery(Component questComponent, ComponentPlayer player) {
        if (!CanQueueQuestRequest(questComponent)
            || player?.PlayerData == null
            || !player.PlayerData.IsMainPlayer) {
            return;
        }

        CompatNetCore.QueueRequest(
            player,
            ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
            (ushort)SubnauticaCompatNetOperation.MermaidQuestDeliverRequest,
            SubnauticaCompatNetPayloads.WriteQuestDeliverRequest(new SubnauticaMermaidQuestDeliverRequest {
                MermaidEntityId = questComponent.Entity.Id
            }));
    }

    public static void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (!NetworkManager.IsServerRunning
            || packet == null
            || playerData?.ComponentPlayer == null
            || packet.Payload == null) {
            return;
        }

        try {
            ComponentPlayer player = playerData.ComponentPlayer;
            switch ((SubnauticaCompatNetOperation)packet.Operation) {
                case SubnauticaCompatNetOperation.MermaidFeedRequest:
                    HandleFeed(SubnauticaCompatNetPayloads.ReadFeedRequest(packet.Payload), player);
                    break;
                case SubnauticaCompatNetOperation.MermaidQuestAcceptRequest:
                    HandleQuestAccept(
                        SubnauticaCompatNetPayloads.ReadQuestAcceptRequest(packet.Payload),
                        player);
                    break;
                case SubnauticaCompatNetOperation.MermaidQuestDeliverRequest:
                    HandleQuestDelivery(
                        SubnauticaCompatNetPayloads.ReadQuestDeliverRequest(packet.Payload),
                        player);
                    break;
                case SubnauticaCompatNetOperation.MermaidGiftRequest:
                    HandleGift(
                        SubnauticaCompatNetPayloads.ReadGiftRequest(packet.Payload),
                        player);
                    break;
                case SubnauticaCompatNetOperation.CreatureKillRequest:
                    HandleCreatureKill(
                        SubnauticaCompatNetPayloads.ReadCreatureKillRequest(packet.Payload),
                        player);
                    break;
            }
        }
        catch {
            // Optional-mod reflection and malformed client payloads must not
            // bring down the multiplayer update loop.
        }
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
            s_forceFullRefresh = false;
        }

        List<SubnauticaMermaidState> changed = [];
        foreach (Entity entity in GameManager.Project.Entities) {
            if (entity == null
                || entity.FindComponent<ComponentPlayer>() != null
                || !TryCapture(entity, out SubnauticaMermaidState state)) {
                continue;
            }

            byte[] statePayload = SubnauticaCompatNetPayloads.Write(
                new SubnauticaMermaidStateBatch {
                    Sequence = 0,
                    FullRefresh = false,
                    Records = [state]
                });
            if (fullRefresh
                || !lastPayloads.TryGetValue(state.MermaidEntityId, out byte[] previous)
                || !previous.AsSpan().SequenceEqual(statePayload)) {
                lastPayloads[state.MermaidEntityId] = statePayload;
                changed.Add(state);
                if (changed.Count >= SubnauticaCompatNetPayloads.MaximumRecordsPerBatch) {
                    break;
                }
            }
        }

        if (changed.Count == 0) {
            return;
        }

        sequence = NextSequence(sequence);
        QueueBatches(changed, sequence, fullRefresh);
    }

    public static void Apply(byte[] payload, ref int lastAppliedSequence) {
        SubnauticaMermaidStateBatch batch;
        try {
            batch = SubnauticaCompatNetPayloads.Read(payload);
        }
        catch {
            return;
        }

        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))) {
            return;
        }

        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        foreach (SubnauticaMermaidState state in batch.Records) {
            if (state == null || state.MermaidEntityId <= 0) {
                continue;
            }

            Entity entity = GameManager.Project?.FindEntity(state.MermaidEntityId);
            if (entity == null || entity.FindComponent<ComponentPlayer>() != null) {
                continue;
            }

            ApplyState(entity, state);
        }
    }

    public static void PublishKraken(
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        ref int sequence,
        ref byte[] lastPayload) {
        if (!NetworkManager.IsServerRunning) {
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

        if (!TryCaptureKrakenState(out SubnauticaKrakenState state)) {
            return;
        }

        byte[] statePayload = SubnauticaCompatNetPayloads.WriteKrakenState(
            new SubnauticaKrakenStateBatch {
                Sequence = 0,
                FullRefresh = false,
                Records = [state]
            });
        if (!fullRefresh
            && lastPayload != null
            && lastPayload.AsSpan().SequenceEqual(statePayload)) {
            return;
        }

        lastPayload = statePayload;
        sequence = NextSequence(sequence);
        CompatNetCore.QueueBroadcast(
            ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
            (ushort)SubnauticaCompatNetOperation.KrakenStateSnapshot,
            SubnauticaCompatNetPayloads.WriteKrakenState(
                new SubnauticaKrakenStateBatch {
                    Sequence = sequence,
                    FullRefresh = fullRefresh,
                    Records = [state]
                }));
    }

    public static void ApplyKraken(byte[] payload, ref int lastAppliedSequence) {
        SubnauticaKrakenStateBatch batch;
        try {
            batch = SubnauticaCompatNetPayloads.ReadKrakenState(payload);
        }
        catch {
            return;
        }

        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))) {
            return;
        }
        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        foreach (SubnauticaKrakenState state in batch.Records) {
            if (state != null) {
                ApplyKrakenState(state);
            }
        }
    }

    private static bool TryCaptureKrakenState(out SubnauticaKrakenState state) {
        state = new SubnauticaKrakenState();
        object manager = GetKrakenManager();
        if (manager == null) {
            return false;
        }

        if (GetFieldValue(manager, "m_triggeredEncounterIds") is IEnumerable triggered) {
            foreach (object value in triggered) {
                if (value is string id
                    && !string.IsNullOrWhiteSpace(id)
                    && id.Length <= SubnauticaCompatNetPayloads.MaximumKrakenEncounterIdLength) {
                    state.TriggeredEncounterIds.Add(id);
                    if (state.TriggeredEncounterIds.Count >= SubnauticaCompatNetPayloads.MaximumKrakenTriggeredIds) {
                        break;
                    }
                }
            }
            state.TriggeredEncounterIds.Sort(StringComparer.OrdinalIgnoreCase);
        }

        object active = GetFieldValue(manager, "m_activeEncounter");
        if (active == null) {
            return true;
        }

        state.HasActiveEncounter = true;
        state.EncounterId = GetStringField(active, "EncounterId");
        state.TriggerPosition = GetFieldValue(active, "TriggerPosition") is Point3 triggerPosition
            ? triggerPosition
            : default;
        state.CenterPosition = GetFieldValue(active, "CenterPosition") is Vector3 center
            ? center
            : Vector3.Zero;
        state.SurfacePosition = GetFieldValue(active, "SurfacePosition") is Vector3 surface
            ? surface
            : Vector3.Zero;
        state.RiseStartPosition = GetFieldValue(active, "RiseStartPosition") is Vector3 riseStart
            ? riseStart
            : Vector3.Zero;
        state.RiseEndPosition = GetFieldValue(active, "RiseEndPosition") is Vector3 riseEnd
            ? riseEnd
            : Vector3.Zero;
        state.TriggerPlayerEntityId = GetIntFieldValue(active, "TriggerPlayerEntityId");
        state.KrakenEntityId = GetIntFieldValue(active, "KrakenEntityId");
        state.Phase = GetIntFieldValue(active, "Phase");
        state.PhaseTimeRemaining = GetFloatFieldValue(active, "PhaseTimeRemaining");
        return true;
    }

    private static void ApplyKrakenState(SubnauticaKrakenState state) {
        object manager = GetKrakenManager();
        if (manager == null) {
            return;
        }

        object triggered = GetFieldValue(manager, "m_triggeredEncounterIds");
        if (triggered is ICollection<string> triggeredIds) {
            triggeredIds.Clear();
            foreach (string id in state.TriggeredEncounterIds) {
                if (!string.IsNullOrWhiteSpace(id)) {
                    triggeredIds.Add(id);
                }
            }
        }

        FieldInfo activeField = FindField(manager.GetType(), "m_activeEncounter");
        if (activeField == null) {
            return;
        }
        if (!state.HasActiveEncounter) {
            SetFieldValue(activeField, manager, null);
            return;
        }

        object active = GetFieldValue(manager, "m_activeEncounter");
        if (active == null) {
            try {
                active = Activator.CreateInstance(activeField.FieldType, nonPublic: true);
            }
            catch {
                return;
            }
            if (active == null) {
                return;
            }
            SetFieldValue(activeField, manager, active);
        }

        SetField(active, "EncounterId", state.EncounterId);
        SetField(active, "TriggerPosition", state.TriggerPosition);
        SetField(active, "CenterPosition", state.CenterPosition);
        SetField(active, "SurfacePosition", state.SurfacePosition);
        SetField(active, "RiseStartPosition", state.RiseStartPosition);
        SetField(active, "RiseEndPosition", state.RiseEndPosition);
        SetField(active, "TriggerPlayerEntityId", state.TriggerPlayerEntityId);
        SetField(active, "KrakenEntityId", state.KrakenEntityId);
        SetEnumField(active, "Phase", state.Phase);
        SetField(active, "PhaseTimeRemaining", state.PhaseTimeRemaining);
        SetField(active, "KrakenEntity", FindEntity(state.KrakenEntityId));
    }

    private static void HandleFeed(
        SubnauticaMermaidFeedRequest request,
        ComponentPlayer player) {
        Entity mermaid = FindEntity(request.MermaidEntityId);
        Component affection = FindComponent(mermaid, MermaidAffectionTypeName);
        if (!CanInteract(player, mermaid) || affection == null) {
            return;
        }

        IInventory inventory = player.ComponentMiner?.Inventory;
        if (inventory == null
            || inventory.ActiveSlotIndex < 0
            || inventory.ActiveSlotIndex >= inventory.SlotsCount
            || inventory.GetSlotCount(inventory.ActiveSlotIndex) <= 0
            || !TryGetFeedKind(inventory.GetSlotValue(inventory.ActiveSlotIndex), out SubnauticaMermaidFeedKind actualKind)
            || actualKind != request.Kind) {
            return;
        }

        int slotIndex = inventory.ActiveSlotIndex;
        int slotValue = inventory.GetSlotValue(slotIndex);
        if (inventory.RemoveSlotItems(slotIndex, 1) != 1) {
            return;
        }

        SetTargetPlayer(affection, player.Entity);
        float points = request.Kind == SubnauticaMermaidFeedKind.CookedFish
            ? 20f
            : 10f;
        if (!TryInvokeFeed(affection, points)) {
            inventory.AddSlotItems(slotIndex, slotValue, 1);
            return;
        }

        InventoryPacketHelpers.MarkInventoryDirty(inventory);
        s_forceFullRefresh = true;
    }

    private static void HandleQuestAccept(
        SubnauticaMermaidQuestAcceptRequest request,
        ComponentPlayer player) {
        Entity mermaid = FindEntity(request.MermaidEntityId);
        Component quest = FindComponent(mermaid, MermaidQuestTypeName);
        Component affection = FindComponent(mermaid, MermaidAffectionTypeName);
        if (!CanInteract(player, mermaid) || quest == null) {
            return;
        }

        SetTargetPlayer(affection, player.Entity);
        SetField(quest, "m_affection", affection);
        if (TryInvokeBool(FindMethod(quest.GetType(), "TryAcceptQuest", 1), quest, [request.QuestId])) {
            s_forceFullRefresh = true;
        }
    }

    private static void HandleQuestDelivery(
        SubnauticaMermaidQuestDeliverRequest request,
        ComponentPlayer player) {
        Entity mermaid = FindEntity(request.MermaidEntityId);
        Component quest = FindComponent(mermaid, MermaidQuestTypeName);
        Component affection = FindComponent(mermaid, MermaidAffectionTypeName);
        if (!CanInteract(player, mermaid) || quest == null) {
            return;
        }

        SetTargetPlayer(affection, player.Entity);
        SetField(quest, "m_affection", affection);
        if (TryInvokeInt(
                FindMethod(quest.GetType(), "TryDeliverItems", 1),
                quest,
                [player],
                out int delivered)
            && delivered > 0) {
            InventoryPacketHelpers.MarkInventoryDirty(player.ComponentMiner?.Inventory);
            s_forceFullRefresh = true;
        }
    }

    public static void PublishAchievements(
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        ref int sequence,
        Dictionary<int, byte[]> lastPayloads) {
        if (!NetworkManager.IsServerRunning) {
            return;
        }

        double now = Time.RealTime;
        if (now < nextPublishTime) {
            return;
        }
        nextPublishTime = now + PublishInterval;
        bool fullRefresh = now >= nextFullRefreshTime;
        if (fullRefresh) {
            nextFullRefreshTime = now + FullRefreshInterval;
        }

        if (!TryCaptureAchievements(out List<SubnauticaAchievementState> states)) {
            return;
        }

        List<SubnauticaAchievementState> changed = [];
        foreach (SubnauticaAchievementState state in states) {
            byte[] statePayload = SubnauticaCompatNetPayloads.WriteAchievementState(
                new SubnauticaAchievementStateBatch {
                    Sequence = 0,
                    FullRefresh = false,
                    Records = [state]
                });
            if (fullRefresh
                || !lastPayloads.TryGetValue(state.PlayerEntityId, out byte[] previous)
                || !previous.AsSpan().SequenceEqual(statePayload)) {
                lastPayloads[state.PlayerEntityId] = statePayload;
                changed.Add(state);
                if (changed.Count >= SubnauticaCompatNetPayloads.MaximumAchievementRecordsPerBatch) {
                    break;
                }
            }
        }

        if (changed.Count == 0) {
            return;
        }

        sequence = NextSequence(sequence);
        QueueAchievementBatches(changed, sequence, fullRefresh);
    }

    public static void ApplyAchievements(byte[] payload, ref int lastAppliedSequence) {
        SubnauticaAchievementStateBatch batch;
        try {
            batch = SubnauticaCompatNetPayloads.ReadAchievementState(payload);
        }
        catch {
            return;
        }

        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))) {
            return;
        }
        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        object manager = GetAchievementManager();
        IDictionary progressByPlayer = GetAchievementProgressDictionary(manager);
        if (progressByPlayer == null) {
            return;
        }

        if (batch.FullRefresh) {
            try {
                progressByPlayer.Clear();
            }
            catch {
                return;
            }
        }

        Type mapType = progressByPlayer.GetType();
        Type progressMapType = mapType.IsGenericType
            ? mapType.GetGenericArguments().Skip(1).FirstOrDefault()
            : null;
        Type progressType = progressMapType?.IsGenericType == true
            ? progressMapType.GetGenericArguments().Skip(1).FirstOrDefault()
            : null;
        if (progressMapType == null || progressType == null) {
            return;
        }

        foreach (SubnauticaAchievementState state in batch.Records) {
            if (state == null || state.PlayerEntityId <= 0) {
                continue;
            }

            if (!progressByPlayer.Contains(state.PlayerEntityId)) {
                try {
                    progressByPlayer[state.PlayerEntityId] = Activator.CreateInstance(progressMapType);
                }
                catch {
                    continue;
                }
            }

            if (progressByPlayer[state.PlayerEntityId] is not IDictionary progress) {
                continue;
            }

            foreach (SubnauticaAchievementProgress value in state.Progress) {
                if (value == null || string.IsNullOrWhiteSpace(value.AchievementId)) {
                    continue;
                }
                object targetProgress;
                try {
                    targetProgress = Activator.CreateInstance(progressType);
                }
                catch {
                    continue;
                }
                SetProperty(targetProgress, "AchievementId", value.AchievementId);
                SetProperty(targetProgress, "CurrentValue", value.CurrentValue);
                SetProperty(targetProgress, "Unlocked", value.Unlocked);
                SetProperty(targetProgress, "UnlockedGameTime", value.UnlockedGameTime);
                SetProperty(targetProgress, "RewardGranted", value.RewardGranted);
                try {
                    progress[value.AchievementId] = targetProgress;
                }
                catch {
                }
            }
        }
    }

    private static bool TryCaptureAchievements(
        out List<SubnauticaAchievementState> states) {
        states = [];
        object manager = GetAchievementManager();
        IDictionary progressByPlayer = GetAchievementProgressDictionary(manager);
        if (progressByPlayer == null) {
            return false;
        }

        foreach (DictionaryEntry playerEntry in progressByPlayer) {
            int playerId;
            try {
                playerId = Convert.ToInt32(playerEntry.Key);
            }
            catch {
                continue;
            }
            if (playerId <= 0 || playerEntry.Value is not IDictionary progressMap) {
                continue;
            }

            SubnauticaAchievementState state = new() { PlayerEntityId = playerId };
            foreach (DictionaryEntry progressEntry in progressMap) {
                object progress = progressEntry.Value;
                string id = GetPropertyValue(
                        FindProperty(progress?.GetType(), "AchievementId"),
                        progress) as string
                    ?? progressEntry.Key as string;
                if (string.IsNullOrWhiteSpace(id)
                    || id.Length > SubnauticaCompatNetPayloads.MaximumAchievementIdLength) {
                    continue;
                }

                state.Progress.Add(new SubnauticaAchievementProgress {
                    AchievementId = id,
                    CurrentValue = GetIntProperty(progress, "CurrentValue"),
                    Unlocked = GetBoolProperty(progress, "Unlocked"),
                    UnlockedGameTime = GetDoubleProperty(progress, "UnlockedGameTime"),
                    RewardGranted = GetBoolProperty(progress, "RewardGranted")
                });
                if (state.Progress.Count >= SubnauticaCompatNetPayloads.MaximumAchievementsPerPlayer) {
                    break;
                }
            }
            states.Add(state);
        }
        return states.Count > 0;
    }

    private static void QueueAchievementBatches(
        List<SubnauticaAchievementState> records,
        int sequence,
        bool fullRefresh) {
        List<SubnauticaAchievementState> current = [];
        foreach (SubnauticaAchievementState record in records) {
            current.Add(record);
            byte[] payload = SubnauticaCompatNetPayloads.WriteAchievementState(
                new SubnauticaAchievementStateBatch {
                    Sequence = sequence,
                    FullRefresh = fullRefresh,
                    Records = current
                });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                QueueAchievementBatch(current, sequence, fullRefresh);
            }
            current = [record];
        }
        if (current.Count > 0) {
            QueueAchievementBatch(current, sequence, fullRefresh);
        }
    }

    private static void QueueAchievementBatch(
        List<SubnauticaAchievementState> records,
        int sequence,
        bool fullRefresh) {
        byte[] payload = SubnauticaCompatNetPayloads.WriteAchievementState(
            new SubnauticaAchievementStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Records = records
            });
        if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
            CompatNetCore.QueueBroadcast(
                ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
                (ushort)SubnauticaCompatNetOperation.AchievementStateSnapshot,
                payload);
        }
    }

    private static void HandleGift(
        SubnauticaMermaidGiftRequest request,
        ComponentPlayer player) {
        Entity mermaid = FindEntity(request.MermaidEntityId);
        Component affection = FindComponent(mermaid, MermaidAffectionTypeName);
        if (!CanInteract(player, mermaid) || affection == null) {
            return;
        }

        SetTargetPlayer(affection, player.Entity);
        MethodInfo method = FindMethod(affection.GetType(), "TryGift", 1);
        ParameterInfo parameter = method?.GetParameters().FirstOrDefault();
        if (method == null || parameter?.ParameterType.IsEnum != true) {
            return;
        }

        try {
            object enumValue = Enum.ToObject(parameter.ParameterType, request.GiftType);
            if (method.Invoke(affection, [enumValue]) is bool success && success) {
                s_forceFullRefresh = true;
            }
        }
        catch {
        }
    }

    /// <summary>
    /// The optional loader reports creature deaths from a client-side damage
    /// prediction as well as from the authoritative server. A pure client
    /// queues the attribution and suppresses its local report; the server
    /// validates the victim and owns the actual quest/achievement mutation.
    /// On a server-originated death the ID is remembered so a late client
    /// relay cannot increment progress a second time.
    /// </summary>
    public static bool TryRelayCreatureDying(
        ComponentHealth componentHealth,
        Injury injury) {
        Entity victim = componentHealth?.Entity;
        if (victim == null) {
            return false;
        }

        if (NetworkManager.IsServerRunning) {
            if (s_serverHandledCreatureKills.Contains(victim.Id)) {
                return true;
            }
            s_serverHandledCreatureKills.Add(victim.Id);
            return false;
        }

        if (!NetworkManager.IsClientRunning
            || injury?.Attacker?.Entity == null) {
            return false;
        }

        ComponentPlayer attacker = injury.Attacker.Entity.FindComponent<ComponentPlayer>();
        ComponentPlayer mainPlayer = FindMainPlayer();
        if (attacker == null || attacker != mainPlayer) {
            return false;
        }

        string creatureName = victim.ValuesDictionary?.DatabaseObject?.Name;
        if (string.IsNullOrWhiteSpace(creatureName)) {
            return false;
        }

        bool queued = CompatNetCore.QueueRequest(
            mainPlayer,
            ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
            (ushort)SubnauticaCompatNetOperation.CreatureKillRequest,
            SubnauticaCompatNetPayloads.WriteCreatureKillRequest(
                new SubnauticaCreatureKillRequest {
                    VictimEntityId = victim.Id,
                    CreatureName = creatureName
                }));
        return queued;
    }

    private static void HandleCreatureKill(
        SubnauticaCreatureKillRequest request,
        ComponentPlayer player) {
        Entity victim = FindEntity(request.VictimEntityId);
        ComponentHealth health = victim?.FindComponent<ComponentHealth>();
        ComponentBody playerBody = player?.Entity?.FindComponent<ComponentCreature>()?.ComponentBody;
        ComponentBody victimBody = victim?.FindComponent<ComponentCreature>()?.ComponentBody;
        if (victim == null
            || health == null
            || victim.FindComponent<ComponentPlayer>() != null
            || !string.Equals(
                victim.ValuesDictionary?.DatabaseObject?.Name,
                request.CreatureName,
                StringComparison.Ordinal)
            || playerBody is not ComponentFrame playerFrame
            || victimBody is not ComponentFrame victimFrame
            || Vector3.DistanceSquared(playerFrame.Position, victimFrame.Position)
                > KillRelayDistanceSquared) {
            return;
        }

        if (!s_serverHandledCreatureKills.Add(victim.Id)) {
            return;
        }

        string creatureName = request.CreatureName;
        IEnumerable<Entity> projectEntities = GameManager.Project == null
            ? Array.Empty<Entity>()
            : GameManager.Project.Entities;
        foreach (Entity entity in projectEntities) {
            Component quest = FindComponent(entity, MermaidQuestTypeName);
            ComponentBody mermaidBody = entity.FindComponent<ComponentCreature>()?.ComponentBody;
            if (quest == null
                || mermaidBody is not ComponentFrame mermaidFrame
                || Vector3.DistanceSquared(mermaidFrame.Position, victimFrame.Position) > 1024f) {
                continue;
            }
            TryInvokeBool(
                FindMethod(quest.GetType(), "ReportKill", 1),
                quest,
                [creatureName]);
        }

        ReportAchievementKill(player.Entity.Id, creatureName);
        s_forceFullRefresh = true;
    }

    private static void ReportAchievementKill(int playerEntityId, string creatureName) {
        Type managerType = FindLoadedType(AchievementManagerTypeName);
        object manager = GetPropertyValue(
            managerType?.GetProperty("Current", StaticFlags),
            null);
        if (manager == null) {
            return;
        }

        Type eventType = FindLoadedType("Subnautica.AchievementEvent");
        Type eventKind = FindLoadedType("Subnautica.AchievementEventType");
        ConstructorInfo constructor = eventType?.GetConstructor([
            eventKind,
            typeof(int),
            typeof(string),
            typeof(int)]);
        MethodInfo report = FindMethod(manager.GetType(), "Report", 1);
        if (constructor == null || report == null || eventKind == null || !eventKind.IsEnum) {
            return;
        }

        try {
            object killKind = Enum.Parse(eventKind, "KillCreature");
            object evt = constructor.Invoke([killKind, playerEntityId, creatureName, 1]);
            report.Invoke(manager, [evt]);
        }
        catch {
        }
    }

    private static bool TryCapture(
        Entity entity,
        out SubnauticaMermaidState state) {
        state = new SubnauticaMermaidState {
            MermaidEntityId = entity.Id
        };
        Component quest = FindComponent(entity, MermaidQuestTypeName);
        Component affection = FindComponent(entity, MermaidAffectionTypeName);
        if (quest == null && affection == null) {
            return false;
        }

        if (affection != null) {
            if (TryGetFloat(FindProperty(affection.GetType(), "AffectionPoints"), affection, out float affectionPoints)) {
                state.AffectionPoints = affectionPoints;
            }
            if (TryGetInt(
                    FindProperty(affection.GetType(), "CurrentState"),
                    affection,
                    out int relationshipState)) {
                state.RelationshipState = relationshipState;
            }
            state.TargetPlayerEntityId = GetEntityId(
                GetPropertyValue(FindProperty(affection.GetType(), "TargetPlayer"), affection));
        }

        if (quest != null) {
            object activeQuest = GetPropertyValue(
                FindProperty(quest.GetType(), "ActiveQuest"),
                quest);
            state.ActiveQuestId = GetStringProperty(activeQuest, "QuestId");
            state.CurrentProgress = GetIntProperty(quest, "CurrentProgress");
            state.TargetProgress = GetIntProperty(quest, "TargetProgress");

            object completed = GetPropertyValue(
                FindProperty(quest.GetType(), "CompletedQuests"),
                quest);
            if (completed is IEnumerable values) {
                foreach (object value in values) {
                    if (value is string questId
                        && !string.IsNullOrWhiteSpace(questId)
                        && questId.Length <= SubnauticaCompatNetPayloads.MaximumQuestIdLength) {
                        state.CompletedQuestIds.Add(questId);
                        if (state.CompletedQuestIds.Count >= SubnauticaCompatNetPayloads.MaximumCompletedQuestIds) {
                            break;
                        }
                    }
                }
            }
        }

        return state.MermaidEntityId > 0;
    }

    private static void ApplyState(Entity entity, SubnauticaMermaidState state) {
        Component affection = FindComponent(entity, MermaidAffectionTypeName);
        if (affection != null) {
            SetProperty(affection, "AffectionPoints", state.AffectionPoints);
            if (state.RelationshipState >= 0) {
                SetEnumProperty(affection, "CurrentState", state.RelationshipState);
            }
            Entity targetPlayer = state.TargetPlayerEntityId > 0
                ? FindEntity(state.TargetPlayerEntityId)
                : null;
            SetProperty(affection, "TargetPlayer", targetPlayer);
        }

        Component quest = FindComponent(entity, MermaidQuestTypeName);
        if (quest == null) {
            return;
        }

        object activeQuest = string.IsNullOrEmpty(state.ActiveQuestId)
            ? null
            : FindQuest(quest.GetType(), state.ActiveQuestId);
        if (string.IsNullOrEmpty(state.ActiveQuestId) || activeQuest != null) {
            SetProperty(quest, "ActiveQuest", activeQuest);
        }
        SetProperty(quest, "CurrentProgress", state.CurrentProgress);
        SetProperty(quest, "TargetProgress", state.TargetProgress);

        PropertyInfo completedProperty = FindProperty(quest.GetType(), "CompletedQuests");
        object completed = GetPropertyValue(completedProperty, quest);
        if (completed is IList completedList) {
            completedList.Clear();
            foreach (string questId in state.CompletedQuestIds) {
                completedList.Add(questId);
            }
        }
    }

    private static void QueueBatches(
        List<SubnauticaMermaidState> records,
        int sequence,
        bool fullRefresh) {
        List<SubnauticaMermaidState> current = [];
        foreach (SubnauticaMermaidState record in records) {
            current.Add(record);
            byte[] payload = SubnauticaCompatNetPayloads.Write(
                new SubnauticaMermaidStateBatch {
                    Sequence = sequence,
                    FullRefresh = fullRefresh,
                    Records = current
                });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                QueueBatch(current, sequence, fullRefresh);
            }
            current = [record];
        }

        if (current.Count > 0) {
            QueueBatch(current, sequence, fullRefresh);
        }
    }

    private static void QueueBatch(
        List<SubnauticaMermaidState> records,
        int sequence,
        bool fullRefresh) {
        byte[] payload = SubnauticaCompatNetPayloads.Write(
            new SubnauticaMermaidStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Records = records
            });
        if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
            CompatNetCore.QueueBroadcast(
                ScopedUpdateGuardCompatNetAdapter.SubnauticaAdapterId,
                (ushort)SubnauticaCompatNetOperation.MermaidStateSnapshot,
                payload);
        }
    }

    private static bool CanQueueQuestRequest(Component questComponent) {
        return NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning
            && questComponent?.Entity != null
            && IsType(questComponent, MermaidQuestTypeName);
    }

    private static ComponentPlayer FindMainPlayer() {
        return GameManager.Project?.FindSubsystem<SubsystemPlayers>()?.MainPlayer;
    }

    private static Entity FindEntity(int entityId) {
        return entityId > 0 ? GameManager.Project?.FindEntity(entityId) : null;
    }

    private static Component FindComponent(Entity entity, string typeName) {
        return entity?.Components.FirstOrDefault(component => IsType(component, typeName));
    }

    private static bool CanInteract(ComponentPlayer player, Entity mermaid) {
        if (player?.Entity == null || mermaid == null || player.Entity == mermaid) {
            return false;
        }

        ComponentBody playerBody = player.Entity.FindComponent<ComponentCreature>()?.ComponentBody;
        ComponentBody mermaidBody = mermaid.FindComponent<ComponentCreature>()?.ComponentBody;
        if (playerBody is not ComponentFrame playerFrame
            || mermaidBody is not ComponentFrame mermaidFrame) {
            return false;
        }

        return Vector3.DistanceSquared(playerFrame.Position, mermaidFrame.Position)
            <= InteractionDistanceSquared;
    }

    private static bool TryGetFeedKind(int value, out SubnauticaMermaidFeedKind kind) {
        kind = default;
        if (value == 0) {
            return false;
        }

        Block block;
        try {
            block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
        }
        catch {
            return false;
        }

        string name = block?.GetType().Name;
        if (string.Equals(name, "RawFishBlock", StringComparison.Ordinal)) {
            kind = SubnauticaMermaidFeedKind.RawFish;
            return true;
        }
        if (string.Equals(name, "CookedFishBlock", StringComparison.Ordinal)) {
            kind = SubnauticaMermaidFeedKind.CookedFish;
            return true;
        }
        return false;
    }

    private static bool TryInvokeFeed(Component affection, float points) {
        MethodInfo method = FindMethod(affection.GetType(), "TryFeed", 2);
        if (method == null) {
            return false;
        }

        object[] arguments = [points, null];
        try {
            return method.Invoke(affection, arguments) is bool success && success;
        }
        catch {
            return false;
        }
    }

    private static bool TryInvokeBool(MethodInfo method, object instance, object[] arguments) {
        try {
            return method?.Invoke(instance, arguments) is bool value && value;
        }
        catch {
            return false;
        }
    }

    private static bool TryInvokeInt(
        MethodInfo method,
        object instance,
        object[] arguments,
        out int value) {
        value = 0;
        try {
            if (method?.Invoke(instance, arguments) is int result) {
                value = result;
                return true;
            }
        }
        catch {
        }
        return false;
    }

    private static bool TryGetFloat(PropertyInfo property, object instance, out float value) {
        value = 0f;
        try {
            if (property?.GetValue(instance) is float result && float.IsFinite(result)) {
                value = result;
                return true;
            }
        }
        catch {
        }
        return false;
    }

    private static bool TryGetInt(PropertyInfo property, object instance, out int value) {
        value = 0;
        try {
            object result = property?.GetValue(instance);
            if (result == null) {
                return false;
            }
            value = Convert.ToInt32(result);
            return true;
        }
        catch {
            return false;
        }
    }

    private static int GetIntProperty(object instance, string name) {
        return TryGetInt(FindProperty(instance?.GetType(), name), instance, out int value)
            ? Math.Max(0, value)
            : 0;
    }

    private static bool GetBoolProperty(object instance, string name) {
        return GetPropertyValue(FindProperty(instance?.GetType(), name), instance) is bool value
            && value;
    }

    private static double GetDoubleProperty(object instance, string name) {
        object value = GetPropertyValue(FindProperty(instance?.GetType(), name), instance);
        try {
            double result = Convert.ToDouble(value);
            return double.IsFinite(result) ? result : 0d;
        }
        catch {
            return 0d;
        }
    }

    private static string GetStringProperty(object instance, string name) {
        return GetPropertyValue(FindProperty(instance?.GetType(), name), instance) as string
            ?? string.Empty;
    }

    private static object GetPropertyValue(PropertyInfo property, object instance) {
        try {
            return property?.GetValue(instance);
        }
        catch {
            return null;
        }
    }

    private static int GetEntityId(object instance) {
        return instance is Entity entity ? entity.Id : 0;
    }

    private static void SetTargetPlayer(Component affection, Entity player) {
        SetProperty(affection, "TargetPlayer", player);
    }

    private static void SetProperty(object instance, string name, object value) {
        PropertyInfo property = FindProperty(instance?.GetType(), name);
        if (property?.CanWrite != true
            || value != null && !property.PropertyType.IsInstanceOfType(value)
            || value == null && property.PropertyType.IsValueType) {
            return;
        }
        try {
            property.SetValue(instance, value);
        }
        catch {
        }
    }

    private static void SetEnumProperty(object instance, string name, int value) {
        PropertyInfo property = FindProperty(instance?.GetType(), name);
        if (property?.CanWrite != true || !property.PropertyType.IsEnum) {
            return;
        }
        try {
            property.SetValue(instance, Enum.ToObject(property.PropertyType, value));
        }
        catch {
        }
    }

    private static object GetKrakenManager() {
        object loader = ModsManager.ModLoaders?.FirstOrDefault(candidate =>
            candidate != null
            && string.Equals(
                candidate.GetType().FullName,
                "Subnautica.SubnauticaLoader",
                StringComparison.Ordinal));
        return GetFieldValue(loader, "m_krakenEncounterManager");
    }

    private static object GetFieldValue(object instance, string name) {
        FieldInfo field = FindField(instance?.GetType(), name);
        try {
            return field?.GetValue(instance);
        }
        catch {
            return null;
        }
    }

    private static FieldInfo FindField(Type type, string name) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            FieldInfo field = current.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        return null;
    }

    private static void SetFieldValue(FieldInfo field, object instance, object value) {
        if (field == null || field.IsInitOnly || field.IsStatic) {
            return;
        }
        try {
            field.SetValue(instance, value);
        }
        catch {
        }
    }

    private static void SetEnumField(object instance, string name, int value) {
        FieldInfo field = FindField(instance?.GetType(), name);
        if (field?.FieldType.IsEnum != true) {
            return;
        }
        try {
            field.SetValue(instance, Enum.ToObject(field.FieldType, value));
        }
        catch {
        }
    }

    private static string GetStringField(object instance, string name) {
        return GetFieldValue(instance, name) as string ?? string.Empty;
    }

    private static int GetIntFieldValue(object instance, string name) {
        try {
            return Convert.ToInt32(GetFieldValue(instance, name));
        }
        catch {
            return 0;
        }
    }

    private static float GetFloatFieldValue(object instance, string name) {
        try {
            float value = Convert.ToSingle(GetFieldValue(instance, name));
            return float.IsFinite(value) && value >= 0f ? value : 0f;
        }
        catch {
            return 0f;
        }
    }

    private static void SetField(object instance, string name, object value) {
        for (Type type = instance?.GetType(); type != null && type != typeof(object); type = type.BaseType) {
            FieldInfo field = type.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field == null
                || field.IsInitOnly
                || value != null && !field.FieldType.IsInstanceOfType(value)) {
                continue;
            }
            try {
                field.SetValue(instance, value);
            }
            catch {
            }
            return;
        }
    }

    private static object GetAchievementManager() {
        Type managerType = FindLoadedType(AchievementManagerTypeName);
        return GetPropertyValue(managerType?.GetProperty("Current", StaticFlags), null);
    }

    private static IDictionary GetAchievementProgressDictionary(object manager) {
        for (Type type = manager?.GetType(); type != null && type != typeof(object); type = type.BaseType) {
            FieldInfo field = type.GetField(
                "m_progressByPlayer",
                InstanceFlags | BindingFlags.DeclaredOnly);
            if (field == null) {
                continue;
            }
            try {
                return field.GetValue(manager) as IDictionary;
            }
            catch {
                return null;
            }
        }
        return null;
    }

    private static Type FindLoadedType(string fullName) {
        if (string.IsNullOrWhiteSpace(fullName)) {
            return null;
        }
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            try {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null) {
                    return type;
                }
            }
            catch {
            }
        }
        return null;
    }

    private static object FindQuest(Type questType, string questId) {
        FieldInfo chainField = questType.GetField("QUEST_CHAIN", StaticFlags | BindingFlags.DeclaredOnly);
        if (chainField?.GetValue(null) is not IEnumerable chain) {
            return null;
        }
        foreach (object quest in chain) {
            if (string.Equals(GetStringProperty(quest, "QuestId"), questId, StringComparison.Ordinal)) {
                return quest;
            }
        }
        return null;
    }

    private static MethodInfo FindMethod(Type type, string name, int parameterCount) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            foreach (MethodInfo method in current.GetMethods(InstanceFlags | BindingFlags.DeclaredOnly)) {
                if ((method.Name == name || method.Name.EndsWith("." + name, StringComparison.Ordinal))
                    && method.GetParameters().Length == parameterCount) {
                    return method;
                }
            }
        }
        return null;
    }

    private static PropertyInfo FindProperty(Type type, string name) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            PropertyInfo property = current.GetProperty(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (property != null) {
                return property;
            }
        }
        return null;
    }

    private static bool IsType(object instance, string typeName) {
        return instance?.GetType().FullName == typeName
            && IsTargetAssembly(instance.GetType().Assembly);
    }

    private static bool IsTargetAssembly(Assembly assembly) {
        if (assembly == null) {
            return false;
        }
        return NamesEqual(assembly.GetName().Name, TargetAssemblyName)
            || NamesEqual(assembly.ManifestModule?.Name, TargetAssemblyName);
    }

    private static bool NamesEqual(string left, string right) {
        return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
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
