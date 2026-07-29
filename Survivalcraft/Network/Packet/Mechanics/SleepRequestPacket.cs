using GameEntitySystem;

namespace Game.Network;

public class SleepRequestPacket : Packet {
    public override byte ID => (byte)PacketType.SleepRequest;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool allowManualWakeup;
    public long m_requestId;

    public SleepRequestPacket() {
    }

    public SleepRequestPacket(ComponentSleep sleep, bool allow) {
        m_entityId = sleep.Entity.Id;
        allowManualWakeup = allow;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref allowManualWakeup);
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
                NetworkMutationDomain.PlayerSleep,
                m_requestId)) {
            return;
        }
        if (!sleep.CanSleep(out string reason)) {
            NetworkManager.Queue(new SleepStatusPacket(
                sleep,
                allowManualWakeup,
                false,
                reason,
                requestId: m_requestId) { To = From });
            return;
        }
        sleep.Sleep(allowManualWakeup, true);
        Project project = sleep.Entity.Project;
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Sleep,
            sleep.Entity.Id);
        NetworkManager.Queue(new SleepStatusPacket(
            sleep,
            allowManualWakeup,
            true,
            revision: revision,
            requestId: m_requestId));
    }
}
