using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;

namespace Game.Network;

public readonly record struct WorldFireStateRecord(
    Point3 Point,
    bool Present,
    float Expandability,
    long Revision);

public readonly record struct WorldFireStateMetrics(
    int AuthoritativeTrackedCells,
    int ClientTrackedCells,
    long AppliedAdds,
    long AppliedRemovals,
    long IgnoredStaleStates,
    long RejectedClientStates,
    int Tombstones);

internal readonly record struct WorldFireStateDecision(
    bool Apply,
    bool Ignored,
    bool Present,
    float Expandability,
    long Revision);

internal sealed class WorldFireStateWindow {
    public bool HasState { get; private set; }

    public bool Present { get; private set; }

    public float Expandability { get; private set; }

    public long AppliedRevision { get; private set; } = -1;

    public WorldFireStateDecision Receive(
        bool present,
        float expandability,
        long revision,
        bool forceRefresh = false) {
        bool stale = revision <= 0
            || HasState && revision < AppliedRevision
            || HasState && revision == AppliedRevision
                && (Present != present || !forceRefresh);
        if (stale) {
            return new WorldFireStateDecision(
                false,
                true,
                Present,
                Expandability,
                AppliedRevision);
        }

        HasState = true;
        Present = present;
        Expandability = present ? expandability : 0f;
        AppliedRevision = revision;
        return new WorldFireStateDecision(
            true,
            false,
            Present,
            Expandability,
            AppliedRevision);
    }
}

public static class WorldFireStateTracker {
    internal const int TombstoneRetentionWindow = 4096;

    private static readonly ConditionalWeakTable<Project, WorldFireStateContext> Contexts = new();

    public static WorldFireStateRecord AdvanceAuthoritative(
        Project project,
        Point3 point,
        bool present,
        float expandability) {
        ArgumentNullException.ThrowIfNull(project);
        WorldFireStateContext context = Context(project);
        long revision = checked(context.HighestAuthoritativeRevision + 1);
        context.HighestAuthoritativeRevision = revision;
        WorldFireStateRecord state = new(
            point,
            present,
            present ? expandability : 0f,
            revision);
        context.AuthoritativeStates[point] = state;
        if (!present) {
            context.AuthoritativeTombstones.Enqueue(
                new WorldFireTombstone(point, revision),
                revision);
        }
        PruneAuthoritativeTombstones(context);
        return state;
    }

    public static WorldFireStateRecord EnsureAuthoritativePresent(
        Project project,
        Point3 point,
        float expandability) {
        ArgumentNullException.ThrowIfNull(project);
        WorldFireStateContext context = Context(project);
        if (context.AuthoritativeStates.TryGetValue(point, out WorldFireStateRecord state)
            && state.Present) {
            return state;
        }
        return AdvanceAuthoritative(project, point, true, expandability);
    }

    public static long GetAuthoritativeRevision(Project project, Point3 point) {
        ArgumentNullException.ThrowIfNull(project);
        return Context(project).AuthoritativeStates.TryGetValue(point, out WorldFireStateRecord state)
            ? state.Revision
            : 0;
    }

    public static long GetAppliedRevision(Project project, Point3 point) {
        ArgumentNullException.ThrowIfNull(project);
        return Context(project).ClientStates.TryGetValue(point, out WorldFireStateWindow window)
            && window.HasState
                ? window.AppliedRevision
                : -1;
    }

    public static IReadOnlyList<WorldFireStateRecord> GetAuthoritativeStates(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        return Context(project).AuthoritativeStates.Values
            .OrderBy(state => state.Point.X)
            .ThenBy(state => state.Point.Y)
            .ThenBy(state => state.Point.Z)
            .ToArray();
    }

    public static IReadOnlyList<WorldFireStateRecord> GetAppliedStates(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        return Context(project).ClientStates
            .Where(pair => pair.Value.HasState)
            .Select(pair => new WorldFireStateRecord(
                pair.Key,
                pair.Value.Present,
                pair.Value.Expandability,
                pair.Value.AppliedRevision))
            .OrderBy(state => state.Point.X)
            .ThenBy(state => state.Point.Y)
            .ThenBy(state => state.Point.Z)
            .ToArray();
    }

    internal static WorldFireStateDecision Receive(
        Project project,
        Point3 point,
        bool present,
        float expandability,
        long revision,
        bool forceRefresh = false) {
        ArgumentNullException.ThrowIfNull(project);
        WorldFireStateContext context = Context(project);
        if (!context.ClientStates.TryGetValue(point, out WorldFireStateWindow window)) {
            if (!forceRefresh && revision <= context.ClientRevisionFloor) {
                context.IgnoredStaleStates++;
                return new WorldFireStateDecision(
                    false,
                    true,
                    false,
                    0f,
                    context.ClientRevisionFloor);
            }
            window = new WorldFireStateWindow();
            context.ClientStates.Add(point, window);
        }
        WorldFireStateDecision decision = window.Receive(
            present,
            expandability,
            revision,
            forceRefresh);
        if (decision.Apply) {
            if (present) {
                context.AppliedAdds++;
            }
            else {
                context.AppliedRemovals++;
                context.ClientTombstones.Enqueue(
                    new WorldFireTombstone(point, decision.Revision),
                    decision.Revision);
            }
            context.HighestAppliedRevision = Math.Max(
                context.HighestAppliedRevision,
                decision.Revision);
            PruneClientTombstones(context);
        }
        else if (decision.Ignored) {
            context.IgnoredStaleStates++;
        }
        return decision;
    }

    public static void RecordRejectedClientState(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        Context(project).RejectedClientStates++;
    }

    public static WorldFireStateMetrics GetMetrics(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        WorldFireStateContext context = Context(project);
        return new WorldFireStateMetrics(
            context.AuthoritativeStates.Count,
            context.ClientStates.Count,
            context.AppliedAdds,
            context.AppliedRemovals,
            context.IgnoredStaleStates,
            context.RejectedClientStates,
            context.ClientStates.Values.Count(window => window.HasState && !window.Present));
    }

    private static WorldFireStateContext Context(Project project) =>
        Contexts.GetValue(project, static _ => new WorldFireStateContext());

    private static void PruneAuthoritativeTombstones(WorldFireStateContext context) {
        long pruneBefore = Math.Max(
            1,
            context.HighestAuthoritativeRevision - TombstoneRetentionWindow + 1);
        while (context.AuthoritativeTombstones.TryPeek(
                   out WorldFireTombstone tombstone,
                   out long revision)
               && revision < pruneBefore) {
            context.AuthoritativeTombstones.Dequeue();
            if (context.AuthoritativeStates.TryGetValue(
                    tombstone.Point,
                    out WorldFireStateRecord state)
                && !state.Present
                && state.Revision == tombstone.Revision) {
                context.AuthoritativeStates.Remove(tombstone.Point);
            }
        }
    }

    private static void PruneClientTombstones(WorldFireStateContext context) {
        long pruneBefore = Math.Max(
            1,
            context.HighestAppliedRevision - TombstoneRetentionWindow + 1);
        context.ClientRevisionFloor = Math.Max(
            context.ClientRevisionFloor,
            pruneBefore - 1);
        while (context.ClientTombstones.TryPeek(
                   out WorldFireTombstone tombstone,
                   out long revision)
               && revision < pruneBefore) {
            context.ClientTombstones.Dequeue();
            if (context.ClientStates.TryGetValue(
                    tombstone.Point,
                    out WorldFireStateWindow window)
                && window.HasState
                && !window.Present
                && window.AppliedRevision == tombstone.Revision) {
                context.ClientStates.Remove(tombstone.Point);
            }
        }
    }

    private readonly record struct WorldFireTombstone(Point3 Point, long Revision);

    private sealed class WorldFireStateContext {
        public Dictionary<Point3, WorldFireStateRecord> AuthoritativeStates { get; } = [];
        public Dictionary<Point3, WorldFireStateWindow> ClientStates { get; } = [];
        public PriorityQueue<WorldFireTombstone, long> AuthoritativeTombstones { get; } = new();
        public PriorityQueue<WorldFireTombstone, long> ClientTombstones { get; } = new();
        public long HighestAuthoritativeRevision;
        public long HighestAppliedRevision;
        public long ClientRevisionFloor;
        public long AppliedAdds;
        public long AppliedRemovals;
        public long IgnoredStaleStates;
        public long RejectedClientStates;
    }
}
