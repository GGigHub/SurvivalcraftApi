using Engine;
using GameEntitySystem;

namespace Game.Network;

public class BehaviorSyncPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorSync;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public const float DestinationEpsilonSquared = 0.0001f;
    public const int MaximumItemCount = 65_536;

    public struct BehaviorSyncItem {
        public int EntityId;
        public long LifecycleRevision;
        public Vector3 BodyPosition;
        public Quaternion BodyRotation;
        public Vector3 BodyVelocity;
        public int ActiveBehaviorIndex;

        public bool HasPathfinding;
        public Vector3? PathfindingDestination;
        public float PathfindingSpeed;
        public float PathfindingRange;
        public int PathfindingMaxPositions;
        public bool PathfindingUseRandomMovements;
        public bool PathfindingIgnoreHeightDifference;
        public bool PathfindingRaycastDestination;
        public int PathfindingDoNotAvoidBodyId;
        public int PathfindingPathVersion;
        public int PathfindingPathCursor;
        public List<Vector3> PathfindingPath;

        public bool HasPilot;
        public Vector3? PilotDestination;
        public float PilotSpeed;
        public float PilotRange;
        public bool PilotIgnoreHeightDifference;
        public bool PilotRaycastDestination;
        public bool PilotTakeRisks;
        public int PilotDoNotAvoidBodyId;

        public bool HasLocomotion;
        public Vector2? WalkOrder;
        public Vector3? FlyOrder;
        public Vector3? SwimOrder;
        public Vector2 TurnOrder;
        public float JumpOrder;
    }

    public List<BehaviorSyncItem> m_items = [];
    public double m_sendTime;

    public BehaviorSyncPacket() {
    }

    public BehaviorSyncPacket(List<ComponentBody> bodies) {
        m_sendTime = Engine.Time.RealTime;
        foreach (ComponentBody body in bodies) {
            if (ShouldSyncBehavior(body)) {
                m_items.Add(CreateItem(body));
            }
        }
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_sendTime);
        int count = archive.IsWriting ? m_items.Count : 0;
        archive.Value(ref count);
        if (count is < 0 or > MaximumItemCount) {
            throw new InvalidDataException($"Invalid behavior sync item count {count}.");
        }
        if (archive.IsReading) {
            m_items.Clear();
            for (int i = 0; i < count; i++) {
                BehaviorSyncItem item = new();
                SerializeItem(archive, ref item);
                m_items.Add(item);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                BehaviorSyncItem item = m_items[i];
                SerializeItem(archive, ref item);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }

        Project project = GameManager.Project;
        HashSet<int> missingEntities = [];
        foreach (BehaviorSyncItem item in m_items) {
            Entity entity = project.FindEntity(item.EntityId);
            if (entity == null) {
                if (EntityLifecycleTracker.ShouldRequestLoad(
                        project,
                        item.EntityId,
                        item.LifecycleRevision,
                        entityExists: false)) {
                    missingEntities.Add(item.EntityId);
                }
                continue;
            }
            if (!EntityLifecycleTracker.CanApplyState(
                    project,
                    item.EntityId,
                    item.LifecycleRevision)) {
                if (EntityLifecycleTracker.ShouldRequestLoad(
                        project,
                        item.EntityId,
                        item.LifecycleRevision,
                        entityExists: true)) {
                    missingEntities.Add(item.EntityId);
                }
                continue;
            }
            ApplyItem(entity, item);
            ComponentBody body = entity.FindComponent<ComponentBody>();
            if (body != null) {
                NetworkBodyInterpolation.PushSnapshot(
                    body,
                    positionChanged: true,
                    item.BodyPosition,
                    rotationChanged: true,
                    item.BodyRotation,
                    velocityChanged: true,
                    item.BodyVelocity,
                    m_sendTime,
                    NetworkBodyInterpolation.RemoteBodyDelay,
                    NetworkBodyInterpolation.RemoteBodyMaxPositionLagSquared);
            }
        }

        if (missingEntities.Count > 0) {
            NetworkManager.Queue(new EntitySyncRequestPacket(missingEntities.ToList()));
        }
    }

    public static readonly Dictionary<ClientSession, List<ComponentBody>> s_behaviorSyncLists = [];
    public static readonly Dictionary<ClientSession, TerrainUpdater.UpdateLocation> s_behaviorSyncLocations = [];
    public static int s_behaviorSyncFrame = -1;
    public static bool s_behaviorSyncFlushQueued;

    public static void CollectBehaviorForSync(ComponentBody body, float dt) {
        if (!NetworkManager.IsServerRunning || !ShouldSyncBehavior(body)) {
            return;
        }
        if (s_behaviorSyncFrame != Time.FrameIndex) {
            if (!Time.PeriodicEvent(0.1, 0.0)) {
                return;
            }
            BeginBehaviorSyncFrame();
        }
        if (s_behaviorSyncLists.Count == 0) {
            return;
        }
        foreach (KeyValuePair<ClientSession, TerrainUpdater.UpdateLocation> pair in s_behaviorSyncLocations) {
            if (SubsystemBodies.IsBodyInRange(body.Position.XZ, pair.Value)) {
                s_behaviorSyncLists[pair.Key].Add(body);
            }
        }
    }

    public static void BeginBehaviorSyncFrame() {
        s_behaviorSyncFrame = Time.FrameIndex;
        s_behaviorSyncLists.Clear();
        s_behaviorSyncLocations.Clear();
        s_behaviorSyncFlushQueued = false;

        if (GameManager.Project == null) {
            return;
        }

        SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (PlayerData playerData in subsystemPlayers.PlayersData) {
            if (playerData.IsMainPlayer) {
                continue;
            }
            ClientSession client = Enumerable.FirstOrDefault(
                NetworkManager.ServerSessions,
                c => c.PlayerIndex == playerData.PlayerIndex);
            if (client == null || client.NetworkState == NetworkState.NotConnected || client.NetworkState < NetworkState.Playing) {
                continue;
            }
            if (subsystemTerrain.TerrainUpdater.UpdateLocations.TryGetValue(playerData.PlayerIndex, out TerrainUpdater.UpdateLocation location)) {
                s_behaviorSyncLists[client] = [];
                s_behaviorSyncLocations[client] = location;
            }
        }

        if (s_behaviorSyncLists.Count > 0 && !s_behaviorSyncFlushQueued) {
            s_behaviorSyncFlushQueued = true;
            Time.QueueFrameIndexDelayedExecution(Time.FrameIndex + 1, FlushBehaviorSyncPackets);
        }
    }

    public static void FlushBehaviorSyncPackets() {
        if (NetworkManager.IsServerRunning) {
            foreach (KeyValuePair<ClientSession, List<ComponentBody>> pair in s_behaviorSyncLists) {
                if (pair.Value.Count > 0 && pair.Key.NetworkState != NetworkState.NotConnected && pair.Key.NetworkState >= NetworkState.Playing) {
                    NetworkManager.Queue(new BehaviorSyncPacket(pair.Value) { To = pair.Key });
                }
            }
        }

        s_behaviorSyncFrame = -1;
        s_behaviorSyncFlushQueued = false;
        s_behaviorSyncLists.Clear();
        s_behaviorSyncLocations.Clear();
    }

    public static void Clear() {
        s_behaviorSyncFrame = -1;
        s_behaviorSyncFlushQueued = false;
        s_behaviorSyncLists.Clear();
        s_behaviorSyncLocations.Clear();
    }

    public static bool ShouldSyncBehavior(ComponentBody body) {
        if (body == null || body.Entity.FindComponent<ComponentPlayer>() != null) {
            return false;
        }
        if (body.Entity.FindComponent<ComponentCreature>() == null || body.Entity.FindComponent<ComponentBehaviorSelector>() == null) {
            return false;
        }
        for (int i = 0; i < body.ChildBodies.Count; i++) {
            if (body.ChildBodies[i].Entity.FindComponent<ComponentPlayer>() != null) {
                return false;
            }
        }
        return true;
    }

    public static BehaviorSyncItem CreateItem(ComponentBody body) {
        ComponentBehaviorSelector selector = body.Entity.FindComponent<ComponentBehaviorSelector>();
        ComponentPathfinding pathfinding = body.Entity.FindComponent<ComponentPathfinding>();
        ComponentPilot pilot = body.Entity.FindComponent<ComponentPilot>();
        ComponentLocomotion locomotion = body.Entity.FindComponent<ComponentLocomotion>();

        BehaviorSyncItem item = new() {
            EntityId = body.Entity.Id,
            LifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
                body.Entity.Project,
                body.Entity.Id),
            BodyPosition = body.Position,
            BodyRotation = body.Rotation,
            BodyVelocity = body.Velocity,
            ActiveBehaviorIndex = GetActiveBehaviorIndex(selector),
            HasPathfinding = pathfinding != null,
            HasPilot = pilot != null,
            HasLocomotion = locomotion != null,
            PathfindingPath = []
        };

        if (pathfinding != null) {
            item.PathfindingDestination = pathfinding.Destination;
            item.PathfindingSpeed = pathfinding.Speed;
            item.PathfindingRange = pathfinding.Range;
            item.PathfindingMaxPositions = pathfinding.MaxPathfindingPositions;
            item.PathfindingUseRandomMovements = pathfinding.UseRandomMovements;
            item.PathfindingIgnoreHeightDifference = pathfinding.IgnoreHeightDifference;
            item.PathfindingRaycastDestination = pathfinding.RaycastDestination;
            item.PathfindingDoNotAvoidBodyId = GetBodyEntityId(pathfinding.DoNotAvoidBody);
            item.PathfindingPathVersion = pathfinding.NetworkPathVersion;
            item.PathfindingPathCursor = pathfinding.NetworkPathCursor;
            for (int i = 0; i < pathfinding.m_pathfindingResult.Path.Count; i++) {
                item.PathfindingPath.Add(pathfinding.m_pathfindingResult.Path.Array[i]);
            }
        }

        if (pilot != null) {
            item.PilotDestination = pilot.Destination;
            item.PilotSpeed = pilot.Speed;
            item.PilotRange = pilot.Range;
            item.PilotIgnoreHeightDifference = pilot.IgnoreHeightDifference;
            item.PilotRaycastDestination = pilot.RaycastDestination;
            item.PilotTakeRisks = pilot.TakeRisks;
            item.PilotDoNotAvoidBodyId = GetBodyEntityId(pilot.DoNotAvoidBody);
        }

        if (locomotion != null) {
            item.WalkOrder = locomotion.LastWalkOrder;
            item.FlyOrder = locomotion.LastFlyOrder;
            item.SwimOrder = locomotion.LastSwimOrder;
            item.TurnOrder = locomotion.LastTurnOrder;
            item.JumpOrder = locomotion.LastJumpOrder;
        }

        return item;
    }

    public static void ApplyItem(Entity entity, BehaviorSyncItem item) {
        ApplyBehaviorState(entity.FindComponent<ComponentBehaviorSelector>(), item.ActiveBehaviorIndex);

        if (item.HasPathfinding) {
            ComponentPathfinding pathfinding = entity.FindComponent<ComponentPathfinding>();
            if (pathfinding != null && IsPathfindingStateChanged(pathfinding, item)) {
                pathfinding.SetDestination(
                    item.PathfindingDestination,
                    item.PathfindingSpeed,
                    item.PathfindingRange,
                    item.PathfindingMaxPositions,
                    item.PathfindingUseRandomMovements,
                    item.PathfindingIgnoreHeightDifference,
                    item.PathfindingRaycastDestination,
                    FindBody(item.PathfindingDoNotAvoidBodyId));
            }
            if (pathfinding != null) {
                pathfinding.ApplyNetworkPath(
                    item.PathfindingPathVersion,
                    item.PathfindingPathCursor,
                    item.PathfindingPath ?? []);
            }
        }

        if (item.HasPilot) {
            ComponentPilot pilot = entity.FindComponent<ComponentPilot>();
            if (pilot != null && IsPilotStateChanged(pilot, item)) {
                pilot.SetDestination(
                    item.PilotDestination,
                    item.PilotSpeed,
                    item.PilotRange,
                    item.PilotIgnoreHeightDifference,
                    item.PilotRaycastDestination,
                    item.PilotTakeRisks,
                    FindBody(item.PilotDoNotAvoidBodyId));
            }
        }

        if (item.HasLocomotion) {
            ComponentLocomotion locomotion = entity.FindComponent<ComponentLocomotion>();
            if (locomotion != null) {
                locomotion.WalkOrder = item.WalkOrder;
                locomotion.FlyOrder = item.FlyOrder;
                locomotion.SwimOrder = item.SwimOrder;
                locomotion.TurnOrder = item.TurnOrder;
                locomotion.JumpOrder = item.JumpOrder;
            }
        }
    }

    public static int GetActiveBehaviorIndex(ComponentBehaviorSelector selector) {
        if (selector == null) {
            return -1;
        }
        for (int i = 0; i < selector.m_behaviors.Count; i++) {
            if (selector.m_behaviors[i].IsActive) {
                return i;
            }
        }
        return -1;
    }

    public static void ApplyBehaviorState(ComponentBehaviorSelector selector, int activeBehaviorIndex) {
        if (selector == null) {
            return;
        }
        for (int i = 0; i < selector.m_behaviors.Count; i++) {
            selector.m_behaviors[i].IsActive = i == activeBehaviorIndex;
        }
    }

    private static void SerializeItem(PacketSerializer archive, ref BehaviorSyncItem item) {
        archive.Value(ref item.EntityId);
        archive.Value(ref item.LifecycleRevision);
        archive.Value(ref item.BodyPosition);
        archive.Value(ref item.BodyRotation);
        archive.Value(ref item.BodyVelocity);
        archive.Value(ref item.ActiveBehaviorIndex);

        archive.Value(ref item.HasPathfinding);
        if (item.HasPathfinding) {
            archive.Value(ref item.PathfindingDestination);
            archive.Value(ref item.PathfindingSpeed);
            archive.Value(ref item.PathfindingRange);
            archive.Value(ref item.PathfindingMaxPositions);
            archive.Value(ref item.PathfindingUseRandomMovements);
            archive.Value(ref item.PathfindingIgnoreHeightDifference);
            archive.Value(ref item.PathfindingRaycastDestination);
            archive.Value(ref item.PathfindingDoNotAvoidBodyId);
            archive.Value(ref item.PathfindingPathVersion);
            archive.Value(ref item.PathfindingPathCursor);
            int pathCount = archive.IsWriting ? item.PathfindingPath?.Count ?? 0 : 0;
            archive.Value(ref pathCount);
            if (archive.IsReading) {
                item.PathfindingPath = [];
            }
            for (int i = 0; i < pathCount; i++) {
                Vector3 point = archive.IsWriting ? item.PathfindingPath[i] : default;
                archive.Value(ref point);
                if (archive.IsReading) {
                    item.PathfindingPath.Add(point);
                }
            }
        }

        archive.Value(ref item.HasPilot);
        if (item.HasPilot) {
            archive.Value(ref item.PilotDestination);
            archive.Value(ref item.PilotSpeed);
            archive.Value(ref item.PilotRange);
            archive.Value(ref item.PilotIgnoreHeightDifference);
            archive.Value(ref item.PilotRaycastDestination);
            archive.Value(ref item.PilotTakeRisks);
            archive.Value(ref item.PilotDoNotAvoidBodyId);
        }

        archive.Value(ref item.HasLocomotion);
        if (item.HasLocomotion) {
            archive.Value(ref item.WalkOrder);
            archive.Value(ref item.FlyOrder);
            archive.Value(ref item.SwimOrder);
            archive.Value(ref item.TurnOrder);
            archive.Value(ref item.JumpOrder);
        }
    }

    public static bool IsPathfindingStateChanged(ComponentPathfinding pathfinding, BehaviorSyncItem item) {
        return HasNullableVector3Changed(pathfinding.Destination, item.PathfindingDestination)
            || pathfinding.Speed != item.PathfindingSpeed
            || pathfinding.Range != item.PathfindingRange
            || pathfinding.MaxPathfindingPositions != item.PathfindingMaxPositions
            || pathfinding.UseRandomMovements != item.PathfindingUseRandomMovements
            || pathfinding.IgnoreHeightDifference != item.PathfindingIgnoreHeightDifference
            || pathfinding.RaycastDestination != item.PathfindingRaycastDestination
            || GetBodyEntityId(pathfinding.DoNotAvoidBody) != item.PathfindingDoNotAvoidBodyId;
    }

    public static bool IsPilotStateChanged(ComponentPilot pilot, BehaviorSyncItem item) {
        return HasNullableVector3Changed(pilot.Destination, item.PilotDestination)
            || pilot.Speed != item.PilotSpeed
            || pilot.Range != item.PilotRange
            || pilot.IgnoreHeightDifference != item.PilotIgnoreHeightDifference
            || pilot.RaycastDestination != item.PilotRaycastDestination
            || pilot.TakeRisks != item.PilotTakeRisks
            || GetBodyEntityId(pilot.DoNotAvoidBody) != item.PilotDoNotAvoidBodyId;
    }

    public static bool HasNullableVector3Changed(Vector3? current, Vector3? next) {
        if (current.HasValue != next.HasValue) {
            return true;
        }
        return current.HasValue && (current.Value - next.Value).LengthSquared() > DestinationEpsilonSquared;
    }

    public static int GetBodyEntityId(ComponentBody body) {
        return body?.Entity?.Id ?? -1;
    }

    public static ComponentBody FindBody(int entityId) {
        if (entityId < 0 || GameManager.Project == null) {
            return null;
        }
        Entity entity = GameManager.Project.FindEntity(entityId);
        return entity?.FindComponent<ComponentBody>();
    }
}
