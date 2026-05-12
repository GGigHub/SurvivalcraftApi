using Engine;

namespace Game.Network;

public class PickableCreatePacket : Packet {
    public override byte ID => (byte)PacketType.PickableCreate;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;
    public Pickable m_pickable = new();

    public PickableCreatePacket() { }

    public PickableCreatePacket(Pickable pickable) {
        m_pickable.Id = pickable.Id;
        m_pickable.Value = pickable.Value;
        m_pickable.Count = pickable.Count;
        m_pickable.Position = pickable.Position;
        m_pickable.Velocity = pickable.Velocity;
        m_pickable.StuckMatrix = pickable.StuckMatrix;
    }

    public override void Serialize(PacketSerializer archive) {
        PickablePacketHelpers.SerializeFullPickable(archive, m_pickable);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePacketHelpers.UpsertPickable(GameManager.Project.FindSubsystem<SubsystemPickables>(), m_pickable);
    }
}

