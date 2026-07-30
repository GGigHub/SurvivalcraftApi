using Engine;
using GameEntitySystem;

namespace Game.Network;

public class EntityOnFirePacket : Packet {
    public override byte ID => (byte)PacketType.EntityOnFire;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;
    public int m_attackerEntityId;
    public bool m_isOnFire;
    public float m_duration;
    public long m_lifecycleRevision;
    public long m_revision;

    public EntityOnFirePacket() {
    }

    public EntityOnFirePacket(
        ComponentOnFire target,
        ComponentCreature attacker = null,
        long revision = -1) {
        m_entityId = target.Entity.Id;
        m_attackerEntityId = attacker?.Entity?.Id ?? 0;
        m_isOnFire = target.IsOnFire;
        m_duration = target.m_fireDuration;
        m_lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            target.Entity.Project,
            target.Entity.Id);
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                target.Entity.Project,
                NetworkStateRevisionDomain.Fire,
                target.Entity.Id);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_attackerEntityId);
        archive.Value(ref m_isOnFire);
        archive.Value(ref m_duration);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || m_entityId <= 0
            || m_attackerEntityId < 0
            || m_revision < 0
            || !float.IsFinite(m_duration)
            || m_duration is < 0f or > 3600f
            || m_isOnFire && m_duration <= 0f
            || !m_isOnFire && m_duration != 0f) {
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
        ComponentOnFire onFire = entity?.FindComponent<ComponentOnFire>();
        if (onFire == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Fire,
            m_entityId,
            m_revision,
            forceRefresh: true);
        if (!decision.Apply) {
            return;
        }
        if (!m_isOnFire) {
            onFire.Extinguish(true);
            return;
        }
        ComponentCreature attacker = m_attackerEntityId == 0
            ? null
            : project.FindEntity(m_attackerEntityId)?.FindComponent<ComponentCreature>();
        onFire.SetOnFire(attacker, m_duration, true);
    }
}
