using Engine;

namespace Game.Network;

public class ApplyImpulsePacket : Packet {
    public override byte ID => (byte)PacketType.ApplyImpulse;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;
    public Vector3 m_impulse;

    public ApplyImpulsePacket() {
    }

    public ApplyImpulsePacket(ComponentBody body, Vector3 impulse) {
        m_entityId = body.Entity.Id;
        m_impulse = impulse;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_impulse);
    }

    public override void Handle(bool isServer) {
        GameManager.Project.FindEntity(m_entityId, e => {
            e.FindComponent<ComponentBody>()?.ApplyImpulse(m_impulse);
        });
    }
}

