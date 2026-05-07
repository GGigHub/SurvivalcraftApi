using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public class HumanRowBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorHumanRow;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_rowLeft;
    public bool m_rowRight;
    public HumanRowBehaviorPackage() { }
    public HumanRowBehaviorPackage(ComponentHumanModel model) {
        m_entityId = model.Entity.Id;
        m_rowLeft = model.m_rowLeft;
        m_rowRight = model.m_rowRight;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_rowLeft);
        archive.Value(ref m_rowRight);
    }
    public override void Handle(bool isServer) {
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentHumanModel humanModel = entity.FindComponent<ComponentHumanModel>();
            if (humanModel != null) {
                humanModel.hasData = true;
                humanModel.m_rowLeft = m_rowLeft;
                humanModel.m_rowRight = m_rowRight;
                Random random = new();
                GameManager.Project.FindSubsystem<SubsystemAudio>().PlayRandomSound("Audio/Rowing", random.Float(0.4f, 0.6f), random.Float(-0.3f, 0.2f), humanModel.m_componentCreature.ComponentBody.Position, 3f, autoDelay: true);
                BehaviorPackageHelpers.BroadcastFromServer(this, isServer);
            }
        });
    }
}