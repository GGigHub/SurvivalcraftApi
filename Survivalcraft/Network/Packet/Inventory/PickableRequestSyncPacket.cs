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
            NetworkService.QueuePacket(new PickableCreatePacket(pick) { ToPlayerIndex = From?.PlayerIndex ?? -1 });
        });
        if (!found) {
            NetworkService.QueuePacket(new PickableDeletePacket(m_id) { ToPlayerIndex = From?.PlayerIndex ?? -1 });
        }
    }
}

