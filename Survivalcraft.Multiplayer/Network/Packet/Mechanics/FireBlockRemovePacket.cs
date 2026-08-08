using Engine;
using GameEntitySystem;

namespace Game.Network;

public class FireBlockRemovePacket : Packet {
    public override byte ID => (byte)PacketType.FireBlockRemove;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public int x;
    public int y;
    public int z;
    public long m_revision;

    public FireBlockRemovePacket() { }

    public FireBlockRemovePacket(int x, int y, int z) {
        this.x = x;
        this.y = y;
        this.z = z;
        Project project = GameManager.Project;
        if (project != null) {
            m_revision = WorldFireStateTracker.AdvanceAuthoritative(
                project,
                new Point3(x, y, z),
                false,
                0f).Revision;
        }
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref x);
        archive.Value(ref y);
        archive.Value(ref z);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        Project project = GameManager.Project;
        if (project == null || !IsValidState()) {
            return;
        }
        if (isServer) {
            WorldFireStateTracker.RecordRejectedClientState(project);
            return;
        }
        Point3 point = new(x, y, z);
        if (WorldFireStateTracker.Receive(
                project,
                point,
                false,
                0f,
                m_revision).Apply) {
            project.FindSubsystem<SubsystemFireBlockBehavior>()
                ?.RemoveFire(x, y, z, true);
        }
    }

    internal bool IsValidState() =>
        FireBlockAddPacket.IsValidPoint(x, y, z)
        && m_revision > 0;
}
