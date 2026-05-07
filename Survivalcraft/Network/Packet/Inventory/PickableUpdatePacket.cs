using Engine;

namespace Game.Network;

public class PickableUpdatePacket : Packet {
    public override byte ID => (byte)PacketType.PickableUpdate;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableUpdatePacket() { }

    public PickableUpdatePacket(List<Pickable> pickables) {
        m_pickables.AddRange(pickables);
    }

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePacketHelpers.ReadList(archive, PickablePacketHelpers.SerializeUpdatePickable);
        }
        else {
            PickablePacketHelpers.WriteList(archive, m_pickables, PickablePacketHelpers.SerializeUpdatePickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            subsystemPickables.PickableAction(pickable.Id, current => current.Position = pickable.Position);
        }
        foreach (Pickable existing in subsystemPickables.Pickables) {
            if (m_pickables.Find(x => x.Id == existing.Id) == null) {
                subsystemPickables.m_pickablesToRemove.Add(existing);
            }
        }
    }
}

