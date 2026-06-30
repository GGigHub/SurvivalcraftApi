using Engine;

namespace Game.Network;

public class PickableRequestSyncPacket : Packet {
    public override byte ID => (byte)PacketType.PickableRequestSync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_id;

    public PickableRequestSyncPacket() { }

    public PickableRequestSyncPacket(int pickId) {
        m_id = pickId;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_id);
    }

    public override void Handle(bool isServer) {
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        bool found = subsystemPickables.PickableAction(m_id, pick => {
            NetworkManager.Queue(new PickableCreatePacket(pick) { To = From });
        });
        if (!found) {
            NetworkManager.Queue(new PickableDeletePacket(m_id) { To = From });
        }
    }
}

