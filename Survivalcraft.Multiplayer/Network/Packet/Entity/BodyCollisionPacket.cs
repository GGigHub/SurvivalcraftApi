using Engine;

namespace Game.Network;

public class BodyCollisionPacket : Packet {
    public override byte ID => (byte)PacketType.BodyCollision;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public const float MaxRequestedMoveDistance = 4f;
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
        if (m_axis < 0
            || m_axis > 2
            || MathF.Abs(m_distance) > MaxRequestedMoveDistance) {
            return;
        }
        if (isServer) {
            ComponentBody body = From?.GetPlayerData()?.ComponentPlayer?.ComponentBody;
            if (body == null) {
                return;
            }
            Vector3 position = body.Position;
            RefreshBodyCollisionBoxes(body, position);
            body.HandleAxisCollision(m_axis, m_distance, ref position, false);
            body.Position = position;
            return;
        }

        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentBody body = e.FindComponent<ComponentBody>();
            if (body != null) {
                Vector3 position = body.Position;
                RefreshBodyCollisionBoxes(body, position);
                body.HandleAxisCollision(m_axis, m_distance, ref position, false);
                body.Position = position;
            }
        });
    }

    private static void RefreshBodyCollisionBoxes(ComponentBody body, Vector3 position) {
        body.m_bodiesCollisionBoxes.Clear();
        if (body.BodyCollidable) {
            body.FindBodiesCollisionBoxes(position, body.m_bodiesCollisionBoxes);
        }
    }
}

