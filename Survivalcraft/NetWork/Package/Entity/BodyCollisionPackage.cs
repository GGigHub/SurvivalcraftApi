using Engine;

namespace Game.NetWork;

public class BodyCollisionPackage : IPackage {
    public override byte ID => (byte)PackageType.BodyCollision;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public int m_axis;
    public float m_distance;

    public BodyCollisionPackage() {
    }

    public BodyCollisionPackage(ComponentBody body, int axis, float distance) {
        m_entityId = body.Entity.Id;
        m_axis = axis;
        m_distance = distance;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_axis);
        archive.Value(ref m_distance);
    }

    public override void Handle(bool isServer) {
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentBody body = e.FindComponent<ComponentBody>();
            if (body != null) {
                Vector3 position = body.Position;
                body.HandleAxisCollision(m_axis, m_distance, ref position, false);
                body.Position = position;
            }
        });
    }
}