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
    private const string MermaidAffectionTypeName = "Subnautica.ComponentMermaidAffection";
    private const string MermaidQuestTypeName = "Subnautica.ComponentMermaidQuest";
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool s_forceFullRefresh;

    public static void Reset() {
        s_forceFullRefresh = false;
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
