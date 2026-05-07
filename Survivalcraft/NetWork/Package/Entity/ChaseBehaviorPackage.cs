using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public class ChaseBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorChase;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isAttack;

    public ChaseBehaviorPackage() { }
    public ChaseBehaviorPackage(ComponentChaseBehavior behavior, bool isAttack) {
        m_entityId = behavior.Entity.Id;
        m_isAttack = isAttack;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isAttack);
    }
    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentChaseBehavior behavior = entity.FindComponent<ComponentChaseBehavior>();
            if (behavior != null) {
                behavior.IsAttack = m_isAttack;
            }
        });
    }
}