using Engine;

namespace Game.Network;

public class PickableDeletePacket : Packet {
    public override byte ID => (byte)PacketType.PickableDelete;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_id;
    public bool PlaySound;
    public long m_revision;

    public PickableDeletePacket() { }

    public PickableDeletePacket(Pickable pickable, long? revision = null) {
        m_id = pickable.Id;
        PlaySound = pickable.PlaySound;
        m_revision = revision ?? PickableLifecycleTracker.GetAuthoritativeRevision(
            GameManager.Project,
            pickable.Id);
    }

    public PickableDeletePacket(int pickId, long? revision = null) {
        m_id = pickId;
        m_revision = revision ?? PickableLifecycleTracker.GetAuthoritativeRevision(
            GameManager.Project,
            pickId);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_id);
        archive.Value(ref PlaySound);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePacketHelpers.TryApplyRemoval(
            GameManager.Project.FindSubsystem<SubsystemPickables>(),
            m_id,
            m_revision,
            PlaySound);
    }
}

