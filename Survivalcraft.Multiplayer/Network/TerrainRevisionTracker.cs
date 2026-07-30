using System.Runtime.CompilerServices;
using Engine;

namespace Game.Network;

public readonly record struct TerrainRevisionMetrics(
    int AuthoritativeTrackedChunks,
    int ClientTrackedChunks,
    long AppliedSnapshots,
    long AppliedChanges,
    long IgnoredStalePackets,
    long IgnoredUnloadedChanges,
    long BufferedChanges,
    long BufferOverflows,
    int PendingChanges);

internal readonly record struct TerrainRevisionCellChange(
    int X,
    int Y,
    int Z,
    int Value,
    long Revision);

internal readonly record struct TerrainRevisionApplyPlan<TChange>(
    bool ApplySnapshot,
    TChange[] Changes,
    bool Ignored,
    bool Buffered,
    bool Overflowed);

internal sealed class TerrainRevisionWindow<TChange> {
    public const int DefaultMaximumPendingChanges = 4096;

    readonly SortedDictionary<long, TChange> m_pending = [];
    readonly int m_maximumPendingChanges;

    public TerrainRevisionWindow(int maximumPendingChanges = DefaultMaximumPendingChanges) {
        if (maximumPendingChanges < 1) {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingChanges));
        }
        m_maximumPendingChanges = maximumPendingChanges;
    }

    public bool HasBaseline { get; private set; }

    public long AppliedRevision { get; private set; } = -1;

    public int PendingCount => m_pending.Count;

    public TerrainRevisionApplyPlan<TChange> ReceiveSnapshot(
        long revision,
        bool forceRefresh = false) {
        if (revision < 0
            || (HasBaseline && revision < AppliedRevision)
            || (HasBaseline && revision == AppliedRevision && !forceRefresh)) {
            return IgnoredPlan();
        }

        HasBaseline = true;
        AppliedRevision = revision;
        foreach (long staleRevision in m_pending.Keys
                     .TakeWhile(candidate => candidate <= revision)
                     .ToArray()) {
            m_pending.Remove(staleRevision);
        }
        return new TerrainRevisionApplyPlan<TChange>(
            ApplySnapshot: true,
            DrainContiguousChanges(),
            Ignored: false,
            Buffered: false,
            Overflowed: false);
    }

    public TerrainRevisionApplyPlan<TChange> ReceiveChange(
        long revision,
        TChange change) {
        if (revision <= 0
            || (HasBaseline && revision <= AppliedRevision)
            || m_pending.ContainsKey(revision)) {
            return IgnoredPlan();
        }

        if (!HasBaseline || revision > AppliedRevision + 1) {
            if (m_pending.Count >= m_maximumPendingChanges) {
                return new TerrainRevisionApplyPlan<TChange>(
                    ApplySnapshot: false,
                    [],
                    Ignored: true,
                    Buffered: false,
                    Overflowed: true);
            }
            m_pending.Add(revision, change);
            return new TerrainRevisionApplyPlan<TChange>(
                ApplySnapshot: false,
                [],
                Ignored: false,
                Buffered: true,
                Overflowed: false);
        }

        AppliedRevision = revision;
        List<TChange> changes = [change];
        changes.AddRange(DrainContiguousChanges());
        return new TerrainRevisionApplyPlan<TChange>(
            ApplySnapshot: false,
            changes.ToArray(),
            Ignored: false,
            Buffered: false,
            Overflowed: false);
    }

    TChange[] DrainContiguousChanges() {
        List<TChange> changes = [];
        while (m_pending.Remove(AppliedRevision + 1, out TChange change)) {
            AppliedRevision++;
            changes.Add(change);
        }
        return changes.ToArray();
    }

    static TerrainRevisionApplyPlan<TChange> IgnoredPlan() =>
        new(
            ApplySnapshot: false,
            [],
            Ignored: true,
            Buffered: false,
            Overflowed: false);
}

public static class TerrainRevisionTracker {
    static readonly ConditionalWeakTable<SubsystemTerrain, TerrainRevisionContext> s_contexts = new();

    public static long AdvanceAuthoritativeRevision(
        SubsystemTerrain terrain,
        int chunkX,
        int chunkZ) {
        ArgumentNullException.ThrowIfNull(terrain);
        TerrainRevisionContext context = Context(terrain);
        Point2 coordinates = new(chunkX, chunkZ);
        context.AuthoritativeRevisions.TryGetValue(coordinates, out long current);
        long revision = checked(current + 1);
        context.AuthoritativeRevisions[coordinates] = revision;
        return revision;
    }

    public static long GetAuthoritativeRevision(
        SubsystemTerrain terrain,
        int chunkX,
        int chunkZ) {
        ArgumentNullException.ThrowIfNull(terrain);
        return Context(terrain).AuthoritativeRevisions.GetValueOrDefault(
            new Point2(chunkX, chunkZ));
    }

    public static long GetAppliedRevision(
        SubsystemTerrain terrain,
        int chunkX,
        int chunkZ) {
        ArgumentNullException.ThrowIfNull(terrain);
        return Context(terrain).ClientWindows.TryGetValue(
                new Point2(chunkX, chunkZ),
                out TerrainRevisionWindow<TerrainRevisionCellChange> window)
            && window.HasBaseline
                ? window.AppliedRevision
                : -1;
    }

    internal static TerrainRevisionApplyPlan<TerrainRevisionCellChange> ReceiveSnapshot(
        SubsystemTerrain terrain,
        int chunkX,
        int chunkZ,
        long revision,
        bool forceRefresh) {
        ArgumentNullException.ThrowIfNull(terrain);
        TerrainRevisionContext context = Context(terrain);
        TerrainRevisionWindow<TerrainRevisionCellChange> window =
            GetOrCreateClientWindow(context, new Point2(chunkX, chunkZ));
        TerrainRevisionApplyPlan<TerrainRevisionCellChange> plan =
            window.ReceiveSnapshot(revision, forceRefresh);
        RecordPlan(context, plan, snapshot: true);
        return plan;
    }

    internal static TerrainRevisionApplyPlan<TerrainRevisionCellChange> ReceiveChange(
        SubsystemTerrain terrain,
        TerrainRevisionCellChange change) {
        ArgumentNullException.ThrowIfNull(terrain);
        TerrainRevisionContext context = Context(terrain);
        if (terrain.Terrain.GetChunkAtCoords(change.X >> 4, change.Z >> 4) == null) {
            context.IgnoredUnloadedChanges++;
            return new TerrainRevisionApplyPlan<TerrainRevisionCellChange>(
                ApplySnapshot: false,
                [],
                Ignored: true,
                Buffered: false,
                Overflowed: false);
        }
        TerrainRevisionWindow<TerrainRevisionCellChange> window =
            GetOrCreateClientWindow(context, new Point2(change.X >> 4, change.Z >> 4));
        TerrainRevisionApplyPlan<TerrainRevisionCellChange> plan =
            window.ReceiveChange(change.Revision, change);
        RecordPlan(context, plan, snapshot: false);
        return plan;
    }

    public static TerrainRevisionMetrics GetMetrics(SubsystemTerrain terrain) {
        ArgumentNullException.ThrowIfNull(terrain);
        TerrainRevisionContext context = Context(terrain);
        return new TerrainRevisionMetrics(
            context.AuthoritativeRevisions.Count,
            context.ClientWindows.Count,
            context.AppliedSnapshots,
            context.AppliedChanges,
            context.IgnoredStalePackets,
            context.IgnoredUnloadedChanges,
            context.BufferedChanges,
            context.BufferOverflows,
            context.ClientWindows.Values.Sum(window => window.PendingCount));
    }

    static TerrainRevisionContext Context(SubsystemTerrain terrain) =>
        s_contexts.GetValue(terrain, _ => new TerrainRevisionContext());

    static TerrainRevisionWindow<TerrainRevisionCellChange> GetOrCreateClientWindow(
        TerrainRevisionContext context,
        Point2 coordinates) {
        if (!context.ClientWindows.TryGetValue(
                coordinates,
                out TerrainRevisionWindow<TerrainRevisionCellChange> window)) {
            window = new TerrainRevisionWindow<TerrainRevisionCellChange>();
            context.ClientWindows.Add(coordinates, window);
        }
        return window;
    }

    static void RecordPlan(
        TerrainRevisionContext context,
        TerrainRevisionApplyPlan<TerrainRevisionCellChange> plan,
        bool snapshot) {
        if (plan.ApplySnapshot && snapshot) {
            context.AppliedSnapshots++;
        }
        context.AppliedChanges += plan.Changes.Length;
        if (plan.Ignored) {
            context.IgnoredStalePackets++;
        }
        if (plan.Buffered) {
            context.BufferedChanges++;
        }
        if (plan.Overflowed) {
            context.BufferOverflows++;
        }
    }

    sealed class TerrainRevisionContext {
        public Dictionary<Point2, long> AuthoritativeRevisions { get; } = [];
        public Dictionary<Point2, TerrainRevisionWindow<TerrainRevisionCellChange>> ClientWindows { get; } = [];
        public long AppliedSnapshots;
        public long AppliedChanges;
        public long IgnoredStalePackets;
        public long IgnoredUnloadedChanges;
        public long BufferedChanges;
        public long BufferOverflows;
    }
}
