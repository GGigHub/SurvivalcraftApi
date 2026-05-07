using Engine;

namespace Game.NetWork;

public class PickableDeletePackage : IPackage {
    public override byte ID => (byte)PackageType.PickableDelete;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_id;
    public bool PlaySound;

    public PickableDeletePackage() { }

    public PickableDeletePackage(Pickable pickable) {
        m_id = pickable.Id;
        PlaySound = pickable.PlaySound;
    }

    public PickableDeletePackage(int pickId) {
        m_id = pickId;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_id);
        archive.Value(ref PlaySound);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePackageHelpers.RemovePickable(GameManager.Project.FindSubsystem<SubsystemPickables>(), m_id, PlaySound);
    }
}