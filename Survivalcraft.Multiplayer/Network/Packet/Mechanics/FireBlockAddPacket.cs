using Engine;
using GameEntitySystem;

namespace Game.Network;

public class FireBlockAddPacket : Packet {
    public const int MaximumBaselineStates = 4096;

    public override byte ID => (byte)PacketType.FireBlockAdd;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public int x;
    public int y;
    public int z;
    public float m_expandability;
    public long m_revision;
    public bool m_forceRefresh;

    public FireBlockAddPacket() { }

    public FireBlockAddPacket(int x, int y, int z, float expandability) {
        this.x = x;
        this.y = y;
        this.z = z;
        m_expandability = expandability;
        Project project = GameManager.Project;
        if (project != null) {
            m_revision = WorldFireStateTracker.AdvanceAuthoritative(
                project,
                new Point3(x, y, z),
                true,
                expandability).Revision;
        }
    }

    private FireBlockAddPacket(WorldFireStateRecord state, bool forceRefresh) {
        x = state.Point.X;
        y = state.Point.Y;
        z = state.Point.Z;
        m_expandability = state.Expandability;
        m_revision = state.Revision;
        m_forceRefresh = forceRefresh;
    }

    public static FireBlockAddPacket CreateBaseline(
        Project project,
        SubsystemFireBlockBehavior.FireData fire) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fire);
        WorldFireStateRecord state = WorldFireStateTracker.EnsureAuthoritativePresent(
            project,
            fire.Point,
            fire.FireExpandability);
        return new FireBlockAddPacket(state, forceRefresh: true);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref x);
        archive.Value(ref y);
        archive.Value(ref z);
        archive.Value(ref m_expandability);
        archive.Value(ref m_revision);
        archive.Value(ref m_forceRefresh);
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
        SubsystemFireBlockBehavior subsystem = project.FindSubsystem<SubsystemFireBlockBehavior>();
        bool forceRefresh = m_forceRefresh
            && subsystem != null
            && !subsystem.m_fireData.ContainsKey(point);
        if (subsystem != null
            && WorldFireStateTracker.Receive(
                project,
                point,
                true,
                m_expandability,
                m_revision,
                forceRefresh).Apply) {
            subsystem.AddFire(x, y, z, m_expandability, true);
        }
    }

    internal bool IsValidState() =>
        IsValidPoint(x, y, z)
        && float.IsFinite(m_expandability)
        && m_expandability is >= 0f and <= 16f
        && m_revision > 0;

    internal static bool IsValidPoint(int x, int y, int z) =>
        y is >= 0 and < 256
        && Math.Abs((long)x) <= 16_777_216
        && Math.Abs((long)z) <= 16_777_216;
}
