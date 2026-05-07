using Engine;

namespace Game.NetWork;

public class PickableDeleteListPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableDeleteList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableDeleteListPackage() { }

    public PickableDeleteListPackage(IEnumerable<Pickable> pickables) {
        m_pickables = [.. pickables];
    }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePackageHelpers.ReadList(archive, PickablePackageHelpers.SerializeDeletePickable);
        }
        else {
            PickablePackageHelpers.WriteList(archive, m_pickables, PickablePackageHelpers.SerializeDeletePickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            PickablePackageHelpers.RemovePickable(subsystemPickables, pickable.Id, false);
        }
    }
}