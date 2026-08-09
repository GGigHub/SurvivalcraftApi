using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Network;

public readonly record struct EntityLifecycleMetrics(
    int AuthoritativeTrackedEntities,
    int ClientTrackedEntities,
    long AppliedLoads,
    long AppliedRemovals,
    long IgnoredStalePackets,
    int Tombstones);

internal readonly record struct EntityLifecycleDecision(
    bool Apply,
    bool Ignored,
    bool Present,
    long Revision);

internal sealed class EntityLifecycleWindow {
    internal const double InitialLoadRequestRetrySeconds = 0.25;
    internal const double MaximumLoadRequestRetrySeconds = 2.0;

    private long requestedRevision = -1;
    private int loadRequestAttempts;
    private double nextLoadRequestTime;

    public bool HasState { get; private set; }

    public bool Present { get; private set; }

    public long AppliedRevision { get; private set; } = -1;

    public EntityLifecycleDecision ReceiveLoad(long revision, bool forceRefresh = false) {
        if (revision < 0
            || HasState && revision < AppliedRevision
            || HasState && revision == AppliedRevision && (!Present || !forceRefresh)) {
            return IgnoredDecision();
        }

        HasState = true;
        Present = true;
        AppliedRevision = revision;
        ClearLoadRequest();
        return AppliedDecision();
    }

    public EntityLifecycleDecision ReceiveRemoval(long revision, bool forceRefresh = false) {
        if (revision < 0
            || HasState && revision < AppliedRevision
            || HasState && revision == AppliedRevision && (Present || !forceRefresh)) {
            return IgnoredDecision();
        }

        HasState = true;
        Present = false;
        AppliedRevision = revision;
        ClearLoadRequest();
        return AppliedDecision();
    }

    public bool CanApplyState(long revision) =>
        HasState && Present && revision == AppliedRevision;

    public bool ShouldRequestLoad(
        long revision,
        bool entityExists,
        double now) {
        if (!double.IsFinite(now)) {
            throw new ArgumentOutOfRangeException(nameof(now));
        }
        bool needsLoad = revision >= 0
            && (!HasState
                || revision > AppliedRevision
                || revision == AppliedRevision && Present && !entityExists);
        if (!needsLoad) {
            ClearLoadRequest();
            return false;
        }
        if (requestedRevision != revision) {
            requestedRevision = revision;
            loadRequestAttempts = 0;
            nextLoadRequestTime = now;
        }
        if (now < nextLoadRequestTime) {
            return false;
        }

        double retryDelay = Math.Min(
            MaximumLoadRequestRetrySeconds,
            InitialLoadRequestRetrySeconds
                * Math.Pow(2, Math.Min(loadRequestAttempts, 3)));
        loadRequestAttempts++;
        nextLoadRequestTime = now + retryDelay;
        return true;
    }

    private void ClearLoadRequest() {
        requestedRevision = -1;
        loadRequestAttempts = 0;
        nextLoadRequestTime = 0;
    }

    private EntityLifecycleDecision AppliedDecision() =>
        new(
            Apply: true,
            Ignored: false,
            Present,
            AppliedRevision);

    private EntityLifecycleDecision IgnoredDecision() =>
        new(
            Apply: false,
            Ignored: true,
            Present,
            AppliedRevision);
}

public static class EntityLifecycleTracker {
    private static readonly ConditionalWeakTable<Project, EntityLifecycleContext> Contexts = new();

    public static long AdvanceAuthoritativeRevision(Project project, int entityId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateEntityId(entityId);
        EntityLifecycleContext context = Context(project);
        context.AuthoritativeRevisions.TryGetValue(entityId, out long current);
        long revision = checked(current + 1);
        context.AuthoritativeRevisions[entityId] = revision;
        return revision;
    }

    public static long GetAuthoritativeRevision(Project project, int entityId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateEntityId(entityId);
        return Context(project).AuthoritativeRevisions.GetValueOrDefault(entityId);
    }

    public static long GetAppliedRevision(Project project, int entityId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateEntityId(entityId);
        return Context(project).ClientWindows.TryGetValue(entityId, out EntityLifecycleWindow window)
            && window.HasState
                ? window.AppliedRevision
                : -1;
    }

    public static bool IsPresent(Project project, int entityId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateEntityId(entityId);
        return Context(project).ClientWindows.TryGetValue(entityId, out EntityLifecycleWindow window)
            && window.HasState
            && window.Present;
    }

    internal static EntityLifecycleDecision ReceiveLoad(
        Project project,
        int entityId,
        long revision,
        bool forceRefresh) {
        EntityLifecycleContext context = Context(project);
        EntityLifecycleDecision decision = GetOrCreateWindow(context, entityId)
            .ReceiveLoad(revision, forceRefresh);
        if (decision.Apply) {
            context.AppliedLoads++;
        }
        else if (decision.Ignored) {
            context.IgnoredStalePackets++;
        }
        return decision;
    }

    internal static EntityLifecycleDecision ReceiveRemoval(
        Project project,
        int entityId,
        long revision,
        bool forceRefresh) {
        EntityLifecycleContext context = Context(project);
        EntityLifecycleDecision decision = GetOrCreateWindow(context, entityId)
            .ReceiveRemoval(revision, forceRefresh);
        if (decision.Apply) {
            context.AppliedRemovals++;
        }
        else if (decision.Ignored) {
            context.IgnoredStalePackets++;
        }
        return decision;
    }

    internal static bool CanApplyState(Project project, int entityId, long revision) {
        EntityLifecycleContext context = Context(project);
        return context.ClientWindows.TryGetValue(entityId, out EntityLifecycleWindow window)
            && window.CanApplyState(revision);
    }

    internal static bool ShouldRequestLoad(
        Project project,
        int entityId,
        long revision,
        bool entityExists) {
        EntityLifecycleContext context = Context(project);
        return GetOrCreateWindow(context, entityId).ShouldRequestLoad(
            revision,
            entityExists,
            Engine.Time.RealTime);
    }

    public static EntityLifecycleMetrics GetMetrics(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        EntityLifecycleContext context = Context(project);
        return new EntityLifecycleMetrics(
            context.AuthoritativeRevisions.Count,
            context.ClientWindows.Count,
            context.AppliedLoads,
            context.AppliedRemovals,
            context.IgnoredStalePackets,
            context.ClientWindows.Values.Count(window => window.HasState && !window.Present));
    }

    private static EntityLifecycleContext Context(Project project) =>
        Contexts.GetValue(project, _ => new EntityLifecycleContext());

    private static EntityLifecycleWindow GetOrCreateWindow(
        EntityLifecycleContext context,
        int entityId) {
        ValidateEntityId(entityId);
        if (!context.ClientWindows.TryGetValue(entityId, out EntityLifecycleWindow window)) {
            window = new EntityLifecycleWindow();
            context.ClientWindows.Add(entityId, window);
        }
        return window;
    }

    private static void ValidateEntityId(int entityId) {
        if (entityId <= 0) {
            throw new ArgumentOutOfRangeException(nameof(entityId));
        }
    }

    private sealed class EntityLifecycleContext {
        public Dictionary<int, long> AuthoritativeRevisions { get; } = [];
        public Dictionary<int, EntityLifecycleWindow> ClientWindows { get; } = [];
        public long AppliedLoads;
        public long AppliedRemovals;
        public long IgnoredStalePackets;
    }
}
