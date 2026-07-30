

using GameEntitySystem;

namespace Game.Network;

public class VitalStatSyncPacket : Packet {
    public override byte ID => (byte)PacketType.VitalStatSync;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public float Food = default;
    public float Stamina = default;
    public float Sleep = default;
    public float Wetness = default;
    public float Temperature = default;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public VitalStatSyncPacket() { }

    public VitalStatSyncPacket(ComponentVitalStats vitalStats, long revision = -1) {
        m_entityId = vitalStats.Entity.Id;
        Food = vitalStats.Food;
        Sleep = vitalStats.Sleep;
        Stamina = vitalStats.Stamina;
        Wetness = vitalStats.Wetness;
        Temperature = vitalStats.Temperature;
        m_lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            vitalStats.Entity.Project,
            vitalStats.Entity.Id);
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                vitalStats.Entity.Project,
                NetworkStateRevisionDomain.VitalStats,
                vitalStats.Entity.Id);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref Food);
        archive.Value(ref Sleep);
        archive.Value(ref Wetness);
        archive.Value(ref Temperature);
        archive.Value(ref Stamina);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || m_entityId <= 0
            || m_revision < 0
            || !float.IsFinite(Food)
            || !float.IsFinite(Sleep)
            || !float.IsFinite(Stamina)
            || !float.IsFinite(Wetness)
            || !float.IsFinite(Temperature)
            || Food is < 0f or > 1f
            || Sleep is < 0f or > 1f
            || Stamina is < 0f or > 1f
            || Wetness is < 0f or > 1f
            || Temperature is < 0f or > 24f) {
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
        ComponentVitalStats vitalStats = entity?.FindComponent<ComponentVitalStats>();
        if (vitalStats == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.VitalStats,
            m_entityId,
            m_revision,
            forceRefresh: true);
        if (!decision.Apply) {
            return;
        }
        vitalStats.Food = Food;
        vitalStats.Sleep = Sleep;
        vitalStats.Wetness = Wetness;
        vitalStats.Temperature = Temperature;
        vitalStats.Stamina = Stamina;
    }
}

