using Engine;

namespace Game.NetWork;

public class PickableRequestSyncPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableRequestSync;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_id;

    public PickableRequestSyncPackage() { }

    public PickableRequestSyncPackage(int pickId) {
        m_id = pickId;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_id);
    }

    public override void Handle(bool isServer) {
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        bool found = subsystemPickables.PickableAction(m_id, pick => {
            NetworkManager.QueuePackage(new PickableCreatePackage(pick) { To = From });
        });
        if (!found) {
            NetworkManager.QueuePackage(new PickableDeletePackage(m_id) { To = From });
        }
    }
}