using Engine;
using GameEntitySystem;

namespace Game.Network;

public class SleepStatusPacket : Packet {
    public override byte ID => (byte)PacketType.SleepStatus;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;
    public bool allowManualWakeup;
    public bool m_result;
    public bool m_isSleeping;
    public string reason = "";
    public long m_lifecycleRevision;
    public long m_revision;
    public long m_requestId;

    public SleepStatusPacket() {
    }

    public SleepStatusPacket(
        ComponentSleep sleep,
        bool allow,
        bool result,
        string msg = "",
        long revision = -1,
        long requestId = 0) {
        m_entityId = sleep.Entity.Id;
        allowManualWakeup = allow;
        m_result = result;
        m_isSleeping = sleep.IsSleeping;
        reason = msg ?? "";
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
        archive.Value(ref allowManualWakeup);
        archive.Value(ref m_result);
        archive.Value(ref m_isSleeping);
        archive.Value(ref reason);
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
        if (!m_result) {
            entity?.FindComponent<ComponentPlayer>()?.ComponentGui
                ?.DisplaySmallMessage(reason, Color.White, false, true);
            return;
        }
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
        if (!decision.Apply) {
            return;
        }
        if (m_isSleeping) {
            sleep.Sleep(allowManualWakeup, true);
        }
        else {
            sleep.WakeUp(true);
        }
    }
}
