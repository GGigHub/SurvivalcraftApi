using Engine;

namespace Game.NetWork;

public class EditSignPackage : IPackage {
    public override byte ID => (byte)PackageType.EditSign;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public Point3 m_point3;

    public EditSignPackage() {
    }

    public EditSignPackage(Point3 point) {
        m_point3 = point;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_point3);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            To = From;
            NetworkManager.QueuePackage(this);
        }
        else {
            DialogsManager.ShowDialog(NetworkManager.MainPlayer.GuiWidget, new EditSignDialog(GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>(), m_point3));
        }
    }
}