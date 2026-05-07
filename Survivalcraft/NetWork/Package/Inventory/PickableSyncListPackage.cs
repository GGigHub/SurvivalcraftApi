using Engine;

namespace Game.NetWork;

public class PickableSyncListPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableSyncList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableSyncListPackage() { }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePackageHelpers.ReadList(archive, PickablePackageHelpers.SerializeFullPickable);
        }
        else {
            PickablePackageHelpers.WriteList(archive, GameManager.Project.FindSubsystem<SubsystemPickables>().Pickables, PickablePackageHelpers.SerializeFullPickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            PickablePackageHelpers.UpsertPickable(subsystemPickables, pickable);
        }
    }
}