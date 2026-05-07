using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public class FishOutOfWaterBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorFishOutOfWater;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isBend;
    public FishOutOfWaterBehaviorPackage() { }
    public FishOutOfWaterBehaviorPackage(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
        m_entityId = behavior.Entity.Id;
        m_isBend = isBend;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isBend);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentFishOutOfWaterBehavior behavior = entity.FindComponent<ComponentFishOutOfWaterBehavior>();
            if (behavior != null) {
                behavior.IsBend = m_isBend;
            }
        });
    }
}