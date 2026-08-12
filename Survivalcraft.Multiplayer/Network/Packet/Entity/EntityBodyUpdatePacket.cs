using Engine;
using GameEntitySystem;

namespace Game.Network;

public class EntityBodyUpdatePacket : Packet {
    public override byte ID => (byte)PacketType.EntityBodyUpdate;
    public override NetworkState MinNeedState => NetworkState.Playing;

    public const float CreatureBodySyncRangeMultiplier = 2f;

    public struct EntityBodySyncItem {
        public int EntityId;
        public bool PositionChanged;
        public Vector3 Position;
        public bool RotationChanged;
        public Quaternion Rotation;
        public bool VelocityChanged;
        public Vector3 Velocity;

        public bool HasBehavior;
        public NetworkBehaviorState Behavior;
        public List<NetworkBehaviorEvent> BehaviorEvents;
    }

    public readonly record struct BodyState(Vector3 Position, Quaternion Rotation, Vector3 Velocity);

    public List<EntityBodySyncItem> m_items = [];
    public double SendTime;
    public int Sequence;

    public static readonly Dictionary<ClientSession, Dictionary<int, BodyState>> s_lastBodyStates = [];
    public static readonly Dictionary<ClientSession, List<ComponentBody>> s_entityBodySyncLists = [];
    public static readonly Dictionary<ClientSession, TerrainUpdater.UpdateLocation> s_entityBodySyncLocations = [];
    public static int s_entityBodySyncFrame = -1;
    public static bool s_entityBodySyncFlushQueued;
    private static int s_nextBehaviorEventSequence;

    public EntityBodyUpdatePacket() {
    }

    public EntityBodyUpdatePacket(List<ComponentBody> bodies, ClientSession session) {
        SendTime = Engine.Time.RealTime;
        Sequence = ++s_nextBehaviorEventSequence;
        if (!s_lastBodyStates.TryGetValue(session, out Dictionary<int, BodyState> lastStates)) {
            lastStates = [];
            s_lastBodyStates[session] = lastStates;
        }
        foreach (ComponentBody body in bodies) {
            EntityBodySyncItem item = CreateItem(body, lastStates);
            if (item.PositionChanged
                || item.RotationChanged
                || item.VelocityChanged
                || item.HasBehavior) {
                m_items.Add(item);
            }
        }
    }

    public EntityBodyUpdatePacket(int entityId, NetworkBehaviorEvent behaviorEvent) {
        SendTime = behaviorEvent.Time;
        Sequence = behaviorEvent.Sequence;
        m_items.Add(new EntityBodySyncItem {
            EntityId = entityId,
            BehaviorEvents = [behaviorEvent]
        });
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SendTime);
        archive.Value(ref Sequence);
        int count = archive.IsWriting ? m_items.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_items.Clear();
            for (int i = 0; i < count; i++) {
                EntityBodySyncItem item = new();
                SerializeItem(archive, ref item);
                m_items.Add(item);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                EntityBodySyncItem item = m_items[i];
                SerializeItem(archive, ref item);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }

        List<int> missingEntities = [];
        foreach (EntityBodySyncItem item in m_items) {
            Entity entity = GameManager.Project.FindEntity(item.EntityId);
            if (entity == null) {
                missingEntities.Add(item.EntityId);
                continue;
            }
            ComponentBody body = entity.FindComponent<ComponentBody>();
            if (body == null) {
                continue;
            }

            NetworkBodyInterpolation.PushSnapshot(
                body,
                item.PositionChanged,
                item.Position,
                item.RotationChanged,
                item.Rotation,
                item.VelocityChanged,
                item.Velocity,
                SendTime,
                NetworkBodyInterpolation.RemoteBodyDelay,
                item.HasBehavior ? item.Behavior : null,
                item.BehaviorEvents,
                Sequence);
        }

        if (missingEntities.Count > 0) {
            NetworkManager.Queue(new EntitySyncRequestPacket(missingEntities));
        }
    }

    public static void CollectBodyForSync(ComponentBody body, float dt) {
        if (!NetworkManager.IsServerRunning || !ShouldSyncBody(body)) {
            return;
        }
        if (s_entityBodySyncFrame != Time.FrameIndex) {
            if (!Time.PeriodicEvent(0.2f, 0.0)) {
                return;
            }
            BeginBodySyncFrame();
        }
        if (s_entityBodySyncLists.Count == 0) {
            return;
        }
        float creatureSyncRange = SettingsManager.VisibilityRange * CreatureBodySyncRangeMultiplier;
        float creatureSyncRangeSquared = MathUtils.Sqr(creatureSyncRange);
        foreach (KeyValuePair<ClientSession, TerrainUpdater.UpdateLocation> pair in s_entityBodySyncLocations) {
            if (Vector2.DistanceSquared(body.Position.XZ, pair.Value.Center) <= creatureSyncRangeSquared) {
                s_entityBodySyncLists[pair.Key].Add(body);
            }
        }
    }

    public static void BeginBodySyncFrame() {
        s_entityBodySyncFrame = Time.FrameIndex;
        s_entityBodySyncLists.Clear();
        s_entityBodySyncLocations.Clear();
        s_entityBodySyncFlushQueued = false;

        if (GameManager.Project == null) {
            return;
        }

        SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (PlayerData playerData in subsystemPlayers.PlayersData) {
            if (playerData.IsMainPlayer) {
                continue;
            }
            ClientSession client = System.Linq.Enumerable.FirstOrDefault(
                NetworkManager.ServerSessions,
                c => c.PlayerIndex == playerData.PlayerIndex);
            if (client == null
                || client.NetworkState == NetworkState.NotConnected
                || client.NetworkState < NetworkState.Playing) {
                continue;
            }
            if (subsystemTerrain.TerrainUpdater.UpdateLocations.TryGetValue(playerData.PlayerIndex, out TerrainUpdater.UpdateLocation location)) {
                s_entityBodySyncLists[client] = [];
                s_entityBodySyncLocations[client] = location;
            }
        }

        if (s_entityBodySyncLists.Count > 0 && !s_entityBodySyncFlushQueued) {
            s_entityBodySyncFlushQueued = true;
            Time.QueueFrameIndexDelayedExecution(Time.FrameIndex + 1, FlushBodySyncPackets);
        }
    }

    public static void QueueBehaviorEvent(
        int entityId,
        NetworkBehaviorEventType type,
        bool value = false,
        bool secondaryValue = false,
        float amount = 0f) {
        if (entityId <= 0) {
            return;
        }

        NetworkBehaviorEvent behaviorEvent = new() {
            Time = Engine.Time.RealTime,
            Sequence = ++s_nextBehaviorEventSequence,
            Type = type,
            Value = value,
            SecondaryValue = secondaryValue,
            Amount = amount
        };
        NetworkManager.Queue(new EntityBodyUpdatePacket(entityId, behaviorEvent));
    }

    public static void FlushBodySyncPackets() {
        if (NetworkManager.IsServerRunning) {
            foreach (KeyValuePair<ClientSession, List<ComponentBody>> pair in s_entityBodySyncLists) {
                if (pair.Value.Count > 0
                    && pair.Key.NetworkState != NetworkState.NotConnected
                    && pair.Key.NetworkState >= NetworkState.Playing) {
                    NetworkManager.Queue(new EntityBodyUpdatePacket(pair.Value, pair.Key) { To = pair.Key });
                }
            }
        }

        s_entityBodySyncFrame = -1;
        s_entityBodySyncFlushQueued = false;
        s_entityBodySyncLists.Clear();
        s_entityBodySyncLocations.Clear();
    }

    public static void Clear() {
        s_entityBodySyncFrame = -1;
        s_entityBodySyncFlushQueued = false;
        s_entityBodySyncLists.Clear();
        s_entityBodySyncLocations.Clear();
        s_lastBodyStates.Clear();
        s_nextBehaviorEventSequence = 0;
    }

    public static void RemoveSnapshotsForSession(ClientSession session) {
        if (session != null) {
            s_lastBodyStates.Remove(session);
        }
    }

    public static bool ShouldSyncBody(ComponentBody body) {
        if (body == null
            || body.Entity.FindComponent<ComponentCreature>() == null
            || body.Entity.FindComponent<ComponentPlayer>() != null) {
            return false;
        }
        for (int i = 0; i < body.ChildBodies.Count; i++) {
            if (body.ChildBodies[i].Entity.FindComponent<ComponentPlayer>() != null) {
                return false;
            }
        }
        return true;
    }

    public static EntityBodySyncItem CreateItem(ComponentBody body, Dictionary<int, BodyState> lastStates) {
        EntityBodySyncItem item = new() {
            EntityId = body.Entity.Id,
            Behavior = new NetworkBehaviorState(),
            BehaviorEvents = []
        };

        if (lastStates.TryGetValue(item.EntityId, out BodyState last)) {
            item.PositionChanged = NetworkBodySyncHelpers.HasVector3Changed(body.Position, last.Position, NetworkBodySyncHelpers.PositionEpsilonSquared);
            item.RotationChanged = NetworkBodySyncHelpers.HasRotationChanged(body.Rotation, last.Rotation);
            item.VelocityChanged = NetworkBodySyncHelpers.HasVector3Changed(body.Velocity, last.Velocity, NetworkBodySyncHelpers.VelocityEpsilonSquared);
        }
        else {
            item.PositionChanged = true;
            item.RotationChanged = true;
            item.VelocityChanged = true;
        }
        if (item.PositionChanged) {
            item.Position = body.Position;
        }
        if (item.RotationChanged) {
            item.Rotation = body.Rotation;
        }
        if (item.VelocityChanged) {
            item.Velocity = body.Velocity;
        }
        lastStates[item.EntityId] = new BodyState(body.Position, body.Rotation, body.Velocity);

        FillBehaviorItem(body, ref item);
        return item;
    }

    private static void FillBehaviorItem(ComponentBody body, ref EntityBodySyncItem item) {
        if (!ShouldSyncBehavior(body)) {
            return;
        }

        ComponentLocomotion locomotion = body.Entity.FindComponent<ComponentLocomotion>();

        item.HasBehavior = true;
        item.Behavior.HasLocomotion = locomotion != null;

        if (locomotion != null) {
            item.Behavior.WalkOrder = locomotion.LastWalkOrder;
            item.Behavior.FlyOrder = locomotion.LastFlyOrder;
            item.Behavior.SwimOrder = locomotion.LastSwimOrder;
            item.Behavior.TurnOrder = locomotion.LastTurnOrder;
            item.Behavior.JumpOrder = locomotion.LastJumpOrder;
        }
    }

    private static bool ShouldSyncBehavior(ComponentBody body) {
        if (body == null
            || body.Entity.FindComponent<ComponentPlayer>() != null
            || body.Entity.FindComponent<ComponentCreature>() == null
            || body.Entity.FindComponent<ComponentBehaviorSelector>() == null) {
            return false;
        }
        for (int i = 0; i < body.ChildBodies.Count; i++) {
            if (body.ChildBodies[i].Entity.FindComponent<ComponentPlayer>() != null) {
                return false;
            }
        }
        return true;
    }

    private static void SerializeItem(PacketSerializer archive, ref EntityBodySyncItem item) {
        archive.Value(ref item.EntityId);
        archive.Value(ref item.PositionChanged);
        if (item.PositionChanged) {
            archive.Value(ref item.Position);
        }
        archive.Value(ref item.RotationChanged);
        if (item.RotationChanged) {
            archive.Value(ref item.Rotation);
        }
        archive.Value(ref item.VelocityChanged);
        if (item.VelocityChanged) {
            archive.Value(ref item.Velocity);
        }

        archive.Value(ref item.HasBehavior);
        if (archive.IsReading) {
            item.Behavior = new NetworkBehaviorState();
            item.BehaviorEvents = [];
        }
        if (item.HasBehavior) {
            archive.Value(ref item.Behavior.HasLocomotion);
            if (item.Behavior.HasLocomotion) {
                archive.Value(ref item.Behavior.WalkOrder);
                archive.Value(ref item.Behavior.FlyOrder);
                archive.Value(ref item.Behavior.SwimOrder);
                archive.Value(ref item.Behavior.TurnOrder);
                archive.Value(ref item.Behavior.JumpOrder);
            }
        }

        int eventCount = archive.IsWriting ? item.BehaviorEvents?.Count ?? 0 : 0;
        archive.Value(ref eventCount);
        if (archive.IsReading) {
            item.BehaviorEvents = [];
        }
        for (int i = 0; i < eventCount; i++) {
            NetworkBehaviorEvent behaviorEvent = archive.IsWriting ? item.BehaviorEvents[i] : default;
            SerializeBehaviorEvent(archive, ref behaviorEvent);
            if (archive.IsReading) {
                item.BehaviorEvents.Add(behaviorEvent);
            }
        }
    }

    private static void SerializeBehaviorEvent(PacketSerializer archive, ref NetworkBehaviorEvent behaviorEvent) {
        archive.Value(ref behaviorEvent.Time);
        archive.Value(ref behaviorEvent.Sequence);
        archive.Enum(ref behaviorEvent.Type);
        archive.Value(ref behaviorEvent.Value);
        archive.Value(ref behaviorEvent.SecondaryValue);
        archive.Value(ref behaviorEvent.Amount);
    }

}
