using Engine;

namespace Game.Network;

public class BodyCollisionPacket : Packet {
    public override byte ID => (byte)PacketType.BodyCollision;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public int m_axis;
    public float m_distance;

    public BodyCollisionPacket() {
    }

    public BodyCollisionPacket(ComponentBody body, int axis, float distance) {
        m_entityId = body.Entity.Id;
        m_axis = axis;
        m_distance = distance;
    }

    public override void Serialize(PacketSerializer archive) {
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

