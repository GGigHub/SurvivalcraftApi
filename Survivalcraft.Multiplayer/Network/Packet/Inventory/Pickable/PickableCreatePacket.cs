using Engine;

namespace Game.Network;

public class PickableCreatePacket : Packet {
    public override byte ID => (byte)PacketType.PickableCreate;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public Pickable m_pickable = new();
    public long m_revision;

    public PickableCreatePacket() { }

    public PickableCreatePacket(Pickable pickable, long? revision = null) {
        m_pickable.Id = pickable.Id;
        m_pickable.Value = pickable.Value;
        m_pickable.Count = pickable.Count;
        m_pickable.Position = pickable.Position;
        m_pickable.Velocity = pickable.Velocity;
        m_pickable.StuckMatrix = pickable.StuckMatrix;
        m_pickable.FlyToPosition = pickable.FlyToPosition;
        m_revision = revision ?? PickableLifecycleTracker.GetAuthoritativeRevision(
            GameManager.Project,
            pickable.Id);
    }

    public override void Serialize(PacketSerializer archive) {
        PickablePacketHelpers.SerializeFullPickable(archive, m_pickable);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePacketHelpers.TryApplyCreate(
            GameManager.Project.FindSubsystem<SubsystemPickables>(),
            m_pickable,
            m_revision);
    }
}

