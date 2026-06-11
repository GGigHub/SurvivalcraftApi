using Engine;

namespace Game.Network;

public class BodyUpdatePacket : Packet {
    public override byte ID => (byte)PacketType.BodyUpdate;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public const float PositionEpsilonSquared = 0.0001f;
    public const float VelocityEpsilonSquared = 0.0001f;
    public const float LookAnglesEpsilonSquared = 0.000001f;
    public const float RotationDotEpsilon = 0.000001f;

    public struct BodySyncSnapshot {
        public bool Initialized;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public Vector2 LookAngles;
        public Vector3? FlyOrder;
    }

    [Flags]
    public enum ChangeFlag : byte {
        None = 0,
        LookAnglesChange = 1,
        FlyOrderChange = 2,
        PositionChange = 4,
        RotationChange = 8,
        VelocityChange = 16
    }

    public struct BodyItem {
        public int CreatureId;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public Vector2 LookAngles;
        public Vector3? FlyOrder;
        public ChangeFlag changeFlag;
    }

    public List<BodyItem> m_bodyList = new List<BodyItem>();

    public BodyUpdatePacket() {
    }

    public BodyUpdatePacket(List<ComponentBody> bodies, ClientSession client) {
        Dictionary<int, BodySyncSnapshot> snapshots = GetBodySyncSnapshots(client);
        foreach (var body in bodies) {
            BodyItem bodyItem = new BodyItem();
            bodyItem.CreatureId = body.Entity.Id;
            snapshots.TryGetValue(body.Entity.Id, out BodySyncSnapshot snapshot);
            if (!snapshot.Initialized || HasVector3Changed(body.Position, snapshot.Position, PositionEpsilonSquared)) {
                bodyItem.changeFlag |= ChangeFlag.PositionChange;
                bodyItem.Position = body.Position;
            }
            if (!snapshot.Initialized || HasRotationChanged(body.Rotation, snapshot.Rotation)) {
                bodyItem.changeFlag |= ChangeFlag.RotationChange;
                bodyItem.Rotation = body.Rotation;
            }
            if (!snapshot.Initialized || HasVector3Changed(body.Velocity, snapshot.Velocity, VelocityEpsilonSquared)) {
                bodyItem.changeFlag |= ChangeFlag.VelocityChange;
                bodyItem.Velocity = body.Velocity;
            }
            if (body.Locomotion != null) {
                if (!snapshot.Initialized || HasVector2Changed(body.Locomotion.LookAngles, snapshot.LookAngles, LookAnglesEpsilonSquared)) {
                    bodyItem.changeFlag |= ChangeFlag.LookAnglesChange;
                    bodyItem.LookAngles = body.Locomotion.LookAngles;
                }
                if (!snapshot.Initialized || body.Locomotion.LastFlyOrder != snapshot.FlyOrder) {
                    bodyItem.changeFlag |= ChangeFlag.FlyOrderChange;
                    bodyItem.FlyOrder = body.Locomotion.LastFlyOrder;
                }
            }
            snapshots[body.Entity.Id] = CreateSnapshot(body);
            m_bodyList.Add(bodyItem);
        }
    }

    public override void Serialize(PacketSerializer archive) {
        int cnt = archive.IsWriting ? m_bodyList.Count : 0;
        archive.Value(ref cnt);
        if (archive.IsReading) {
            m_bodyList.Clear();
            for (int i = 0; i < cnt; i++) {
                BodyItem item = new BodyItem();
                SerializeItem(archive, ref item);
                m_bodyList.Add(item);
            }
        }
        else {
            for (int i = 0; i < cnt; i++) {
                BodyItem item = m_bodyList[i];
                SerializeItem(archive, ref item);
            }
        }
    }

    private void SerializeItem(PacketSerializer archive, ref BodyItem item) {
        archive.Enum(ref item.changeFlag);
        archive.Value(ref item.CreatureId);
        if (item.changeFlag.HasFlag(ChangeFlag.PositionChange)) archive.Value(ref item.Position);
        if (item.changeFlag.HasFlag(ChangeFlag.RotationChange)) archive.Value(ref item.Rotation);
        if (item.changeFlag.HasFlag(ChangeFlag.VelocityChange)) archive.Value(ref item.Velocity);
        if (item.changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) archive.Value(ref item.LookAngles);
        if (item.changeFlag.HasFlag(ChangeFlag.FlyOrderChange)) archive.Value(ref item.FlyOrder);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var bodies = GameManager.Project.FindSubsystem<SubsystemBodies>();
        List<int> ml = new List<int>();
        List<ComponentBody> rl = new List<ComponentBody>();
        foreach (var item in m_bodyList) {
            bodies.FindBodyByCreatureID(item.CreatureId, body => {
                if (item.changeFlag.HasFlag(ChangeFlag.PositionChange)) {
                    body.Position = item.Position;
                }
                if (item.changeFlag.HasFlag(ChangeFlag.RotationChange)) {
                    body.Rotation = item.Rotation;
                }
                if (item.changeFlag.HasFlag(ChangeFlag.VelocityChange)) {
                    body.Velocity = item.Velocity;
                }
                if (body.Locomotion != null) {
                    if (item.changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) {
                        body.Locomotion.LookAngles = item.LookAngles;
                    }
                    if (item.changeFlag.HasFlag(ChangeFlag.FlyOrderChange)) body.Locomotion.LastFlyOrder = item.FlyOrder;
                }
            }, () => {
                ml.Add(item.CreatureId);
            });
        }
        foreach (var item2 in bodies.Bodies) {
            if (item2.Entity.FindComponent<ComponentPlayer>() != null || item2.ChildBodies.Count > 0) continue;
            if (!m_bodyList.Exists(x => x.CreatureId == item2.Entity.Id)) {
                rl.Add(item2);
            }
        }
        if (ml.Count > 0) {
            NetworkManager.Queue(new EntitySyncRequestPacket(ml));
        }
        if (rl.Count > 0) {
            foreach (var b in rl) {
                GameManager.Project.RemoveEntity(b.Entity, true);
            }
        }
    }

    public static bool ShouldBeNetworkControlled(ComponentBody body) {
        if (body == null || (!NetworkManager.IsNetworkMode && !NetworkManager.IsServerMode)) {
            return false;
        }

        ComponentPlayer componentPlayer = body.Entity.FindComponent<ComponentPlayer>();
        bool isMainPlayer = componentPlayer?.PlayerData?.IsMainPlayer == true;

        bool hasChildPlayer = false;
        bool hasNonMainChildPlayer = false;
        for (int i = 0; i < body.ChildBodies.Count; i++) {
            ComponentPlayer childPlayer = body.ChildBodies[i].Entity.FindComponent<ComponentPlayer>();
            if (childPlayer == null) {
                continue;
            }
            hasChildPlayer = true;
            if (childPlayer.PlayerData?.IsMainPlayer != true) {
                hasNonMainChildPlayer = true;
                break;
            }
        }

        if (NetworkManager.IsNetworkMode) {
            return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
                || (componentPlayer == null && !hasChildPlayer)
                || hasNonMainChildPlayer;
        }

        return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
            || (componentPlayer == null && hasNonMainChildPlayer);
    }

    public static readonly Dictionary<ClientSession, List<ComponentBody>> s_bodySyncLists = [];
    public static readonly Dictionary<ClientSession, TerrainUpdater.UpdateLocation> s_bodySyncLocations = [];
    public static readonly Dictionary<ClientSession, Dictionary<int, BodySyncSnapshot>> s_bodySyncSnapshots = [];
    public static int s_bodySyncFrame = -1;
    public static bool s_bodySyncFlushQueued;

    public static void CollectBodyForSync(ComponentBody body, float dt) {
        if (!NetworkManager.IsServerMode || !Time.PeriodicEvent(0.1, 0.0) || !ShouldSyncBody(body)) {
            return;
        }
        if (s_bodySyncFrame != Time.FrameIndex) {
            BeginBodySyncFrame();
        }
        if (s_bodySyncLists.Count == 0) {
            return;
        }
        foreach (KeyValuePair<ClientSession, TerrainUpdater.UpdateLocation> pair in s_bodySyncLocations) {
            if (SubsystemBodies.IsBodyInRange(body.Position.XZ, pair.Value)) {
                s_bodySyncLists[pair.Key].Add(body);
            }
        }
    }

    public static void BeginBodySyncFrame() {
        s_bodySyncFrame = Time.FrameIndex;
        s_bodySyncLists.Clear();
        s_bodySyncLocations.Clear();
        s_bodySyncFlushQueued = false;

        if (GameManager.Project == null) {
            return;
        }

        if (GameManager.Project == null) {
            return;
        }

        SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (PlayerData playerData in subsystemPlayers.PlayersData) {
            if (playerData.IsMainPlayer) continue;
            // The sender should not send to the player who triggered the update, so we compare playerIndex
            ClientSession client = Enumerable.FirstOrDefault(
                        NetworkManager.NetworkServer?.ClientSessions.Values, c => c.PlayerIndex == playerData.PlayerIndex);
            if (client == null || client.NetworkState == NetworkState.NotConnected || client.NetworkState < NetworkState.Playing) {
                continue;
            }
            if (subsystemTerrain.TerrainUpdater.UpdateLocations.TryGetValue(playerData.PlayerIndex, out TerrainUpdater.UpdateLocation location)) {
                s_bodySyncLists[client] = [];
                s_bodySyncLocations[client] = location;
                GetBodySyncSnapshots(client);
            }
        }

        if (s_bodySyncLists.Count > 0 && !s_bodySyncFlushQueued) {
            s_bodySyncFlushQueued = true;
            Time.QueueFrameIndexDelayedExecution(Time.FrameIndex + 1, FlushBodySyncPackets);
        }
    }

    public static void FlushBodySyncPackets() {
        if (NetworkManager.IsServerMode) {
            foreach (KeyValuePair<ClientSession, List<ComponentBody>> pair in s_bodySyncLists) {
                if (pair.Key.NetworkState != NetworkState.NotConnected && pair.Key.NetworkState >= NetworkState.Playing) {
                    NetworkManager.Queue(new BodyUpdatePacket(pair.Value, pair.Key) { To = pair.Key });
                }
            }
        }

        s_bodySyncFrame = -1;
        s_bodySyncFlushQueued = false;
        s_bodySyncLists.Clear();
        s_bodySyncLocations.Clear();
    }

    public static bool ShouldSyncBody(ComponentBody body) {
        return body.Entity.FindComponent<ComponentPlayer>() == null && body.ChildBodies.Count == 0;
    }

    public static Dictionary<int, BodySyncSnapshot> GetBodySyncSnapshots(ClientSession client) {
        if (!s_bodySyncSnapshots.TryGetValue(client, out Dictionary<int, BodySyncSnapshot> snapshots)) {
            snapshots = [];
            s_bodySyncSnapshots[client] = snapshots;
        }
        return snapshots;
    }

    public static BodySyncSnapshot CreateSnapshot(ComponentBody body) {
        return new BodySyncSnapshot {
            Initialized = true,
            Position = body.Position,
            Rotation = body.Rotation,
            Velocity = body.Velocity,
            LookAngles = body.Locomotion?.LookAngles ?? Vector2.Zero,
            FlyOrder = body.Locomotion?.LastFlyOrder
        };
    }

    public static bool HasVector3Changed(Vector3 current, Vector3 previous, float epsilonSquared) {
        return (current - previous).LengthSquared() > epsilonSquared;
    }

    public static bool HasVector2Changed(Vector2 current, Vector2 previous, float epsilonSquared) {
        return (current - previous).LengthSquared() > epsilonSquared;
    }

    public static bool HasRotationChanged(Quaternion current, Quaternion previous) {
        return 1f - MathF.Abs(Quaternion.Dot(current, previous)) > RotationDotEpsilon;
    }
}

