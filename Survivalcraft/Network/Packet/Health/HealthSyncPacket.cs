using Engine;
using GameEntitySystem;

namespace Game.Network;

public class HealthSyncPacket : Packet {
    public override byte ID => (byte)PacketType.HealthSync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_targetId;
    public float m_health;
    public string m_cause = "";
    public bool m_hasDeathTime;
    public double m_deathTime;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public HealthSyncPacket() {
    }

    public HealthSyncPacket(ComponentHealth health, long revision = -1) {
        m_targetId = health.Entity.Id;
        m_health = health.Health;
        m_cause = health.CauseOfDeath ?? "";
        m_hasDeathTime = health.DeathTime.HasValue;
        m_deathTime = health.DeathTime ?? 0;
        m_lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            health.Entity.Project,
            health.Entity.Id);
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                health.Entity.Project,
                NetworkStateRevisionDomain.Health,
                health.Entity.Id);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
        archive.Value(ref m_cause);
        archive.Value(ref m_hasDeathTime);
        archive.Value(ref m_deathTime);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || m_targetId <= 0
            || m_revision < 0
            || !float.IsFinite(m_health)
            || m_health is < 0f or > 1f
            || m_hasDeathTime && !double.IsFinite(m_deathTime)) return;
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
        ComponentHealth health = entity?.FindComponent<ComponentHealth>();
        if (health == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Health,
            m_targetId,
            m_revision,
            forceRefresh: true);
        if (decision.Apply) {
            health.Health = m_health;
            health.CauseOfDeath = string.IsNullOrEmpty(m_cause) ? null : m_cause;
            health.DeathTime = m_hasDeathTime ? m_deathTime : null;
        }
    }
}

