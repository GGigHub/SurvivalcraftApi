

namespace Game.NetWork;

public class ComponentSicknessPackage : IPackage {
    public override byte ID => (byte)PackageType.Sickness;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public enum EventType {
        SyncStat,
        NauseaEffect
    }

    public float m_sicknessDuration;
    public bool m_isNausea;
    public EventType m_eventType;
    public int entityId;

    public ComponentSicknessPackage() {
    }

    public ComponentSicknessPackage(ComponentSickness sickness) {
        entityId = sickness.Entity.Id;
        m_sicknessDuration = sickness.m_sicknessDuration;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref entityId);
        archive.Value(ref m_sicknessDuration);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(entityId, entity => {
            var sickness = entity.FindComponent<ComponentSickness>();
            if (sickness != null) {
                sickness.m_sicknessDuration = m_sicknessDuration;
                if (m_sicknessDuration > 0f) {
                    sickness.NauseaEffect();
                }
            }
        });
    }
}