using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public class DigInMudBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorDigInMud;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isDigIn;
    public DigInMudBehaviorPackage() { }
    public DigInMudBehaviorPackage(ComponentDigInMudBehavior behavior, bool isDigIn) {
        m_entityId = behavior.Entity.Id;
        m_isDigIn = isDigIn;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isDigIn);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentDigInMudBehavior behavior = entity.FindComponent<ComponentDigInMudBehavior>();
            if (behavior != null) {
                behavior.IsDigIn = m_isDigIn;
            }
        });
    }
}