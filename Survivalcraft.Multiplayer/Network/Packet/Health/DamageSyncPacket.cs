using Engine;
using GameEntitySystem;

namespace Game.Network;

public class DamageSyncPacket : Packet {
    public override byte ID => (byte)PacketType.DamageSync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_targetId;
    public float m_health;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public DamageSyncPacket() {
    }

    public DamageSyncPacket(ComponentDamage health, long revision = -1) {
        m_targetId = health.Entity.Id;
        m_health = health.Hitpoints;
        m_lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            health.Entity.Project,
            health.Entity.Id);
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                health.Entity.Project,
                NetworkStateRevisionDomain.Damage,
                health.Entity.Id);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || m_targetId <= 0
            || m_revision < 0
            || !float.IsFinite(m_health)
            || m_health < 0f) return;
        Project project = GameManager.Project;
        Entity entity = project?.FindEntity(m_targetId);
        if (project == null
            || !EntityLifecycleTracker.CanApplyState(
                project,
                m_targetId,
                m_lifecycleRevision)) {
            if (project != null
                && EntityLifecycleTracker.ShouldRequestLoad(
                    project,
                    m_targetId,
                    m_lifecycleRevision,
                    entity != null)) {
                NetworkManager.Queue(new EntitySyncRequestPacket([m_targetId]));
            }
            return;
        }
        ComponentDamage damage = entity?.FindComponent<ComponentDamage>();
        if (damage == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Damage,
            m_targetId,
            m_revision,
            forceRefresh: true);
        if (decision.Apply) {
            damage.Hitpoints = m_health;
        }
    }
}

