using Engine;

namespace Game.NetWork;

public class PickableCreatePackage : IPackage {
    public override byte ID => (byte)PackageType.PickableCreate;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public Pickable m_pickable = new();

    public PickableCreatePackage() { }

    public PickableCreatePackage(Pickable pickable) {
        m_pickable.Id = pickable.Id;
        m_pickable.Value = pickable.Value;
        m_pickable.Count = pickable.Count;
        m_pickable.Position = pickable.Position;
        m_pickable.Velocity = pickable.Velocity;
        m_pickable.StuckMatrix = pickable.StuckMatrix;
    }

    public override void Serialize(PackageSerializer archive) {
        PickablePackageHelpers.SerializeFullPickable(archive, m_pickable);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePackageHelpers.UpsertPickable(GameManager.Project.FindSubsystem<SubsystemPickables>(), m_pickable);
    }
}