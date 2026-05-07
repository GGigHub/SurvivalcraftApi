using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public class EatPickableBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorEatPickable;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public EatPickableBehaviorPackage() { }
    public EatPickableBehaviorPackage(ComponentEatPickableBehavior behavior, bool isFeed) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentEatPickableBehavior behavior = entity.FindComponent<ComponentEatPickableBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}