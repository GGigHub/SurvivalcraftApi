using Engine;

namespace Game.NetWork;

public class DamageSyncPackage : IPackage {
    public override byte ID => (byte)PackageType.DamageSync;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public float m_health;

    public DamageSyncPackage() {
    }

    public DamageSyncPackage(ComponentDamage health) {
        m_targetId = health.Entity.Id;
        m_health = health.Hitpoints;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_targetId, e => {
            ComponentDamage h = e.FindComponent<ComponentDamage>();
            if (h != null) h.Hitpoints = m_health;
        });
    }
}