using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Network;

public readonly record struct PickableLifecycleMetrics(
    int AuthoritativeTrackedPickables,
    int ClientTrackedPickables,
    long AppliedCreates,
    long AppliedUpdates,
    long AppliedRemovals,
    long IgnoredStalePackets,
    long MissingUpdateRequests,
    int Tombstones);

internal readonly record struct PickableLifecycleDecision(
    bool Apply,
    bool Ignored,
    bool Present,
    long Revision);

internal sealed class PickableLifecycleWindow {
    public bool HasState { get; private set; }

    public bool Present { get; private set; }

    public long AppliedRevision { get; private set; } = -1;

    public PickableLifecycleDecision ReceiveCreate(
        long revision,
        bool forceRefresh = false) {
        if (revision < 0
            || HasState && revision < AppliedRevision
            || HasState && revision == AppliedRevision && (!Present || !forceRefresh)) {
            return IgnoredDecision();
        }

        HasState = true;
        Present = true;
        AppliedRevision = revision;
        return AppliedDecision();
    }

    public PickableLifecycleDecision ReceiveUpdate(long revision) {
        if (revision < 0
            || !HasState
            || !Present
            || revision <= AppliedRevision) {
            return IgnoredDecision();
        }

        AppliedRevision = revision;
        return AppliedDecision();
    }

    public PickableLifecycleDecision ReceiveRemoval(
        long revision,
        bool forceRefresh = false) {
        if (revision < 0
            || HasState && revision < AppliedRevision
            || HasState && revision == AppliedRevision && (Present || !forceRefresh)) {
            return IgnoredDecision();
        }

        HasState = true;
        Present = false;
        AppliedRevision = revision;
        return AppliedDecision();
    }

    private PickableLifecycleDecision AppliedDecision() =>
        new(true, false, Present, AppliedRevision);

    private PickableLifecycleDecision IgnoredDecision() =>
        new(false, true, Present, AppliedRevision);
}

public static class PickableLifecycleTracker {
    private static readonly ConditionalWeakTable<Project, PickableLifecycleContext> Contexts = new();

    public static long AdvanceAuthoritativeRevision(Project project, int pickableId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidatePickableId(pickableId);
        PickableLifecycleContext context = Context(project);
        context.AuthoritativeRevisions.TryGetValue(pickableId, out long current);
        long revision = checked(current + 1);
        context.AuthoritativeRevisions[pickableId] = revision;
        return revision;
    }

    public static long GetAuthoritativeRevision(Project project, int pickableId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidatePickableId(pickableId);
        return Context(project).AuthoritativeRevisions.GetValueOrDefault(pickableId);
    }

    public static long GetAppliedRevision(Project project, int pickableId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidatePickableId(pickableId);
        return Context(project).ClientWindows.TryGetValue(
                pickableId,
                out PickableLifecycleWindow window)
            && window.HasState
                ? window.AppliedRevision
                : -1;
    }

    public static bool IsPresent(Project project, int pickableId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidatePickableId(pickableId);
        return Context(project).ClientWindows.TryGetValue(
                pickableId,
                out PickableLifecycleWindow window)
            && window.HasState
            && window.Present;
    }

    internal static PickableLifecycleDecision ReceiveCreate(
        Project project,
        int pickableId,
        long revision,
        bool forceRefresh) {
        PickableLifecycleContext context = Context(project);
        PickableLifecycleDecision decision = GetOrCreateWindow(context, pickableId)
            .ReceiveCreate(revision, forceRefresh);
        RecordDecision(context, decision, PickableOperation.Create);
        return decision;
    }

    internal static PickableLifecycleDecision ReceiveUpdate(
        Project project,
        int pickableId,
        long revision) {
        PickableLifecycleContext context = Context(project);
        PickableLifecycleDecision decision = GetOrCreateWindow(context, pickableId)
            .ReceiveUpdate(revision);
        RecordDecision(context, decision, PickableOperation.Update);
        return decision;
    }

    internal static PickableLifecycleDecision ReceiveRemoval(
        Project project,
        int pickableId,
        long revision,
        bool forceRefresh) {
        PickableLifecycleContext context = Context(project);
        PickableLifecycleDecision decision = GetOrCreateWindow(context, pickableId)
            .ReceiveRemoval(revision, forceRefresh);
        RecordDecision(context, decision, PickableOperation.Remove);
        return decision;
    }

    internal static void RecordMissingUpdateRequest(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        Context(project).MissingUpdateRequests++;
    }

    public static PickableLifecycleMetrics GetMetrics(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        PickableLifecycleContext context = Context(project);
        return new PickableLifecycleMetrics(
            context.AuthoritativeRevisions.Count,
            context.ClientWindows.Count,
            context.AppliedCreates,
            context.AppliedUpdates,
            context.AppliedRemovals,
            context.IgnoredStalePackets,
            context.MissingUpdateRequests,
            context.ClientWindows.Values.Count(window => window.HasState && !window.Present));
    }

    private static PickableLifecycleContext Context(Project project) =>
        Contexts.GetValue(project, static _ => new PickableLifecycleContext());

    private static PickableLifecycleWindow GetOrCreateWindow(
        PickableLifecycleContext context,
        int pickableId) {
        ValidatePickableId(pickableId);
        if (!context.ClientWindows.TryGetValue(
                pickableId,
                out PickableLifecycleWindow window)) {
            window = new PickableLifecycleWindow();
            context.ClientWindows.Add(pickableId, window);
        }
        return window;
    }

    private static void RecordDecision(
        PickableLifecycleContext context,
        PickableLifecycleDecision decision,
        PickableOperation operation) {
        if (decision.Apply) {
            switch (operation) {
                case PickableOperation.Create:
                    context.AppliedCreates++;
                    break;
                case PickableOperation.Update:
                    context.AppliedUpdates++;
                    break;
                case PickableOperation.Remove:
                    context.AppliedRemovals++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
        else if (decision.Ignored) {
            context.IgnoredStalePackets++;
        }
    }

    private static void ValidatePickableId(int pickableId) {
        if (pickableId <= 0) {
            throw new ArgumentOutOfRangeException(nameof(pickableId));
        }
    }

    private enum PickableOperation {
        Create,
        Update,
        Remove
    }

    private sealed class PickableLifecycleContext {
        public Dictionary<int, long> AuthoritativeRevisions { get; } = [];
        public Dictionary<int, PickableLifecycleWindow> ClientWindows { get; } = [];
        public long AppliedCreates;
        public long AppliedUpdates;
        public long AppliedRemovals;
        public long IgnoredStalePackets;
        public long MissingUpdateRequests;
    }
}
