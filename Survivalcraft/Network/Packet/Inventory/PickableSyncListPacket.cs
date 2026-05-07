using Engine;

namespace Game.Network;

public class PickableSyncListPacket : Packet {
    public override byte ID => (byte)PacketType.PickableSyncList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableSyncListPacket() { }

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePacketHelpers.ReadList(archive, PickablePacketHelpers.SerializeFullPickable);
        }
        else {
            PickablePacketHelpers.WriteList(archive, GameManager.Project.FindSubsystem<SubsystemPickables>().Pickables, PickablePacketHelpers.SerializeFullPickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            PickablePacketHelpers.UpsertPickable(subsystemPickables, pickable);
        }
    }
}

