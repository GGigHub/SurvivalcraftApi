using Engine;
using GameEntitySystem;

namespace Game.Network;

public class InjureEventPacket : Packet {
    public override byte ID => (byte)PacketType.InjureEvent;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_targetId;
    public int m_attackerId;
    public float m_amount;
    public float m_health;
    public string m_cause = "";
    public bool m_ignoreInvulnerability;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public InjureEventPacket() {
    }

    public InjureEventPacket(ComponentHealth target, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability, long revision = -1) {
        m_targetId = target.Entity.Id;
        m_attackerId = attacker?.Entity.Id ?? 0;
        m_amount = amount;
        m_health = target.Health;
        m_cause = cause;
        m_ignoreInvulnerability = ignoreInvulnerability;
        m_lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            target.Entity.Project,
            target.Entity.Id);
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                target.Entity.Project,
                NetworkStateRevisionDomain.Health,
                target.Entity.Id);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_attackerId);
        archive.Value(ref m_health);
        archive.Value(ref m_amount);
        archive.Value(ref m_cause);
        archive.Value(ref m_ignoreInvulnerability);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || m_targetId <= 0
            || m_revision < 0
            || !float.IsFinite(m_amount)
            || m_amount <= 0f
            || !float.IsFinite(m_health)
            || m_health is < 0f or > 1f) return;
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
            m_revision);
        if (!decision.Apply) {
            return;
        }
        ComponentCreature attacker = m_attackerId > 0
            ? project.FindEntity(m_attackerId)?.FindComponent<ComponentCreature>()
            : null;
        health.Injure(
            m_amount,
            attacker,
            m_ignoreInvulnerability,
            m_cause,
            true);
        health.Health = m_health;
        if (m_health <= 0f) {
            health.CauseOfDeath = m_cause;
        }
    }
}

