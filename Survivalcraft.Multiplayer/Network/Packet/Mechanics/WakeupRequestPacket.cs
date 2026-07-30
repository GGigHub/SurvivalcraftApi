using GameEntitySystem;

namespace Game.Network;

public class WakeupRequestPacket : Packet {
    public override byte ID => (byte)PacketType.WakeupRequest;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public long m_requestId;

    public WakeupRequestPacket() {
    }

    public WakeupRequestPacket(ComponentSleep sleep) {
        m_entityId = sleep.Entity.Id;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (!isServer) {
            return;
        }
        ComponentPlayer player = From?.GetPlayerData()?.ComponentPlayer;
        ComponentSleep sleep = player?.Entity?.FindComponent<ComponentSleep>();
        if (From == null
            || From.NetworkState != NetworkState.Playing
            || player == null
            || sleep == null
            || m_entityId <= 0
            || sleep.Entity.Id != m_entityId
            || !NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.PlayerWakeup,
                m_requestId)
            || !sleep.IsSleeping) {
            return;
        }
        sleep.WakeUp(true);
        Project project = sleep.Entity.Project;
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Sleep,
            sleep.Entity.Id);
        NetworkManager.Queue(new WakeupStatusPacket(sleep, revision, m_requestId));
    }
}
