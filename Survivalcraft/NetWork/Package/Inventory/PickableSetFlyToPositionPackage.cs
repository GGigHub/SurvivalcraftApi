using Engine;

namespace Game.NetWork;

public class PickableSetFlyToPositionPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableSetFlyToPosition;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_id;
    public Vector3? m_flyToPosition;

    public PickableSetFlyToPositionPackage() { }

    public PickableSetFlyToPositionPackage(Pickable pickable) {
        m_id = pickable.Id;
        m_flyToPosition = pickable.FlyToPosition;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_id);
        Vector3 flyTo = m_flyToPosition ?? default;
        archive.Value(ref flyTo);
        if (archive.IsReading) {
            m_flyToPosition = flyTo;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemPickables>().PickableAction(m_id, pick => pick.FlyToPosition = m_flyToPosition);
    }
}