using Engine;

namespace Game.NetWork;

public class HealthSyncPackage : IPackage {
    public override byte ID => (byte)PackageType.HealthSync;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public float m_health;
    public string m_cause = "";

    public HealthSyncPackage() {
    }

    public HealthSyncPackage(ComponentHealth health) {
        m_targetId = health.Entity.Id;
        m_health = health.Health;
        m_cause = health.CauseOfDeath ?? "";
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
        archive.Value(ref m_cause);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_targetId, e => {
            ComponentHealth health = e.FindComponent<ComponentHealth>();
            if (health != null) {
                health.Health = m_health;
                health.CauseOfDeath = m_cause;
            }
        });
    }
}