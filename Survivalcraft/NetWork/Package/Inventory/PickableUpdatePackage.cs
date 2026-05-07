using Engine;

namespace Game.NetWork;

public class PickableUpdatePackage : IPackage {
    public override byte ID => (byte)PackageType.PickableUpdate;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableUpdatePackage() { }

    public PickableUpdatePackage(List<Pickable> pickables) {
        m_pickables.AddRange(pickables);
    }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePackageHelpers.ReadList(archive, PickablePackageHelpers.SerializeUpdatePickable);
        }
        else {
            PickablePackageHelpers.WriteList(archive, m_pickables, PickablePackageHelpers.SerializeUpdatePickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            subsystemPickables.PickableAction(pickable.Id, current => current.Position = pickable.Position);
        }
        foreach (Pickable existing in subsystemPickables.Pickables) {
            if (m_pickables.Find(x => x.Id == existing.Id) == null) {
                subsystemPickables.m_pickablesToRemove.Add(existing);
            }
        }
    }
}