using Engine;

namespace Game.Network;

public class PickableDeletePacket : Packet {
    public override byte ID => (byte)PacketType.PickableDelete;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_id;
    public bool PlaySound;

    public PickableDeletePacket() { }

    public PickableDeletePacket(Pickable pickable) {
        m_id = pickable.Id;
        PlaySound = pickable.PlaySound;
    }

    public PickableDeletePacket(int pickId) {
        m_id = pickId;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_id);
        archive.Value(ref PlaySound);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePacketHelpers.RemovePickable(GameManager.Project.FindSubsystem<SubsystemPickables>(), m_id, PlaySound);
    }
}

