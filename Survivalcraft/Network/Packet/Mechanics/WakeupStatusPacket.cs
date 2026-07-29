using GameEntitySystem;

namespace Game.Network;

public class WakeupStatusPacket : Packet {
    public override byte ID => (byte)PacketType.WakeupStatus;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;
    public long m_lifecycleRevision;
    public long m_revision;
    public long m_requestId;

    public WakeupStatusPacket() {
    }

    public WakeupStatusPacket(ComponentSleep sleep, long revision = -1, long requestId = 0) {
        m_entityId = sleep.Entity.Id;
        m_lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            sleep.Entity.Project,
            sleep.Entity.Id);
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                sleep.Entity.Project,
                NetworkStateRevisionDomain.Sleep,
                sleep.Entity.Id);
        m_requestId = requestId;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (isServer || m_entityId <= 0 || m_revision < 0) {
            return;
        }
        Project project = GameManager.Project;
        Entity entity = project?.FindEntity(m_entityId);
        if (project == null
            || !EntityLifecycleTracker.CanApplyState(
                project,
                m_entityId,
                m_lifecycleRevision)) {
            if (project != null
                && EntityLifecycleTracker.ShouldRequestLoad(
                    project,
                    m_entityId,
                    m_lifecycleRevision,
                    entity != null)) {
                NetworkManager.Queue(new EntitySyncRequestPacket([m_entityId]));
            }
            return;
        }
        ComponentSleep sleep = entity?.FindComponent<ComponentSleep>();
        if (sleep == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Sleep,
            m_entityId,
            m_revision,
            forceRefresh: true);
        if (decision.Apply) {
            sleep.WakeUp(true);
        }
    }
}
