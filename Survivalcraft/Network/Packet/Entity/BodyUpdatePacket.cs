using Engine;

namespace Game.Network;

public class BodyUpdatePacket : Packet {
    public override byte ID => (byte)PacketType.BodyUpdate;
    public override NetworkState MinNeedState => NetworkState.Playing;

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

    public BodyUpdatePacket(List<ComponentBody> bodies) {
        foreach (var body in bodies) {
            BodyItem bodyItem = new BodyItem();
            bodyItem.CreatureId = body.Entity.Id;
            if (body.SendPosition.HasValue) {
                bodyItem.changeFlag |= ChangeFlag.PositionChange;
                bodyItem.Position = body.SendPosition.Value;
            }
            if (body.SendRotation.HasValue) {
                bodyItem.changeFlag |= ChangeFlag.RotationChange;
                bodyItem.Rotation = body.SendRotation.Value;
            }
            if (body.SendVelocity.HasValue) {
                bodyItem.changeFlag |= ChangeFlag.VelocityChange;
                bodyItem.Velocity = body.SendVelocity.Value;
            }
            if (body.Locomotion != null) {
                if (body.Locomotion.SendLookAngles.HasValue) {
                    bodyItem.changeFlag |= ChangeFlag.LookAnglesChange;
                    bodyItem.LookAngles = body.Locomotion.SendLookAngles.Value;
                }
                if (body.Locomotion.FlyOrderChange) {
                    bodyItem.changeFlag |= ChangeFlag.FlyOrderChange;
                    bodyItem.FlyOrder = body.Locomotion.LastFlyOrder;
                }
            }
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
                if (item.changeFlag.HasFlag(ChangeFlag.PositionChange)) body.netPosition.SetNext(item.Position);
                if (item.changeFlag.HasFlag(ChangeFlag.RotationChange)) body.netRotation.SetNext(item.Rotation);
                if (item.changeFlag.HasFlag(ChangeFlag.VelocityChange)) body.netVelocity.SetNext(item.Velocity);
                if (body.Locomotion != null) {
                    if (item.changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) body.Locomotion.NetLookAngles.SetNext(item.LookAngles);
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
            NetworkService.QueuePacket(new EntitySyncRequestPacket(ml));
        }
        if (rl.Count > 0) {
            foreach (var b in rl) {
                GameManager.Project.RemoveEntity(b.Entity, true);
            }
        }
    }

    public static bool ShouldBeNetworkControlled(ComponentBody body) {
        if (body == null || (!NetworkService.IsClient && !NetworkService.IsServer)) {
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

        if (NetworkService.IsClient) {
            return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
                || (componentPlayer == null && !hasChildPlayer)
                || hasNonMainChildPlayer;
        }

        return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
            || (componentPlayer == null && hasNonMainChildPlayer);
    }

    public static readonly Dictionary<Client, List<ComponentBody>> s_bodySyncLists = [];
    public static readonly Dictionary<Client, TerrainUpdater.UpdateLocation> s_bodySyncLocations = [];
    public static readonly HashSet<ComponentBody> s_bodySyncResetBodies = [];
    public static int s_bodySyncFrame = -1;
    public static bool s_bodySyncFlushQueued;

    public static void CollectBodyForSync(ComponentBody body, float dt) {
        if (!NetworkService.IsServer || !Time.PeriodicEvent(0.1, 0.0) || !ShouldSyncBody(body)) {
            return;
        }
        if (s_bodySyncFrame != Time.FrameIndex) {
            BeginBodySyncFrame();
        }
        if (s_bodySyncLists.Count == 0) {
            return;
        }
        foreach (KeyValuePair<Client, TerrainUpdater.UpdateLocation> pair in s_bodySyncLocations) {
            if (SubsystemBodies.IsBodyInRange(body.Position.XZ, pair.Value)) {
                s_bodySyncLists[pair.Key].Add(body);
            }
        }
    }

    public static void BeginBodySyncFrame() {
        s_bodySyncFrame = Time.FrameIndex;
        s_bodySyncLists.Clear();
        s_bodySyncLocations.Clear();
        s_bodySyncResetBodies.Clear();
        s_bodySyncFlushQueued = false;

        if (GameManager.Project == null) {
            return;
        }

        SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (PlayerData playerData in subsystemPlayers.PlayersData) {
            if (playerData.IsMainPlayer || playerData.ComponentPlayer == null) {
                continue;
            }
            Client client = NetworkService.GetClientByGUID(playerData.NetworkGuid);
            if (client == null || !client.IsConnected || client.State < NetworkState.Playing) {
                continue;
            }
            if (subsystemTerrain.TerrainUpdater.UpdateLocations.TryGetValue(playerData.PlayerIndex, out TerrainUpdater.UpdateLocation location)) {
                s_bodySyncLists[client] = [];
                s_bodySyncLocations[client] = location;
            }
        }

        if (s_bodySyncLists.Count > 0 && !s_bodySyncFlushQueued) {
            s_bodySyncFlushQueued = true;
            Time.QueueFrameIndexDelayedExecution(Time.FrameIndex + 1, FlushBodySyncPackets);
        }
    }

    public static void FlushBodySyncPackets() {
        if (NetworkService.IsServer) {
            foreach (KeyValuePair<Client, List<ComponentBody>> pair in s_bodySyncLists) {
                if (pair.Key.IsConnected && pair.Key.State >= NetworkState.Playing) {
                    NetworkService.QueuePacket(new BodyUpdatePacket(pair.Value) { To = pair.Key });
                    foreach (ComponentBody body in pair.Value) {
                        s_bodySyncResetBodies.Add(body);
                    }
                }
            }
            foreach (ComponentBody body in s_bodySyncResetBodies) {
                ResetBodySyncFlags(body);
            }
        }

        s_bodySyncFrame = -1;
        s_bodySyncFlushQueued = false;
        s_bodySyncLists.Clear();
        s_bodySyncLocations.Clear();
        s_bodySyncResetBodies.Clear();
    }

    public static bool ShouldSyncBody(ComponentBody body) {
        return body.Entity.FindComponent<ComponentPlayer>() == null && body.ChildBodies.Count == 0;
    }

    public static void ResetBodySyncFlags(ComponentBody body) {
        body.SendPosition = null;
        body.SendRotation = null;
        body.SendVelocity = null;
        if (body.Locomotion != null) {
            body.Locomotion.SendLookAngles = null;
            body.Locomotion.FlyOrderChange = false;
        }
    }
}

