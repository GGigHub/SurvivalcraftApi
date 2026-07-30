using Engine;

namespace Game.Network;

public class PickableSetFlyToPositionPacket : Packet {
    public override byte ID => (byte)PacketType.PickableSetFlyToPosition;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_id;
    public Vector3? m_flyToPosition;

    public PickableSetFlyToPositionPacket() { }

    public PickableSetFlyToPositionPacket(Pickable pickable) {
        m_id = pickable.Id;
        m_flyToPosition = pickable.FlyToPosition;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_id);
        bool hasFlyToPosition = archive.IsWriting && m_flyToPosition.HasValue;
        archive.Value(ref hasFlyToPosition);
        Vector3 flyTo = m_flyToPosition ?? default;
        if (hasFlyToPosition) {
            archive.Value(ref flyTo);
        }
        if (archive.IsReading) {
            m_flyToPosition = hasFlyToPosition ? flyTo : null;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemPickables>().PickableAction(m_id, pick => pick.FlyToPosition = m_flyToPosition);
    }
}

