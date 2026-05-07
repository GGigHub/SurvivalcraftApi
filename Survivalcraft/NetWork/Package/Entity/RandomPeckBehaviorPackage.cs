using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public class RandomPeckBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorRandomPeck;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public RandomPeckBehaviorPackage() { }
    public RandomPeckBehaviorPackage(ComponentRandomPeckBehavior behavior, bool isFeed) {
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
            ComponentRandomPeckBehavior behavior = entity.FindComponent<ComponentRandomPeckBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}