using Engine;

namespace Game.Network;

public class PickableDeleteListPacket : Packet {
    public override byte ID => (byte)PacketType.PickableDeleteList;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableDeleteListPacket() { }

    public PickableDeleteListPacket(IEnumerable<Pickable> pickables) {
        m_pickables = [.. pickables];
    }

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePacketHelpers.ReadList(archive, PickablePacketHelpers.SerializeDeletePickable);
        }
        else {
            PickablePacketHelpers.WriteList(archive, m_pickables, PickablePacketHelpers.SerializeDeletePickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            PickablePacketHelpers.RemovePickable(subsystemPickables, pickable.Id, false);
        }
    }
}

