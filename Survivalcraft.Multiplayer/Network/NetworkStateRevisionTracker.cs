using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Network;

public enum NetworkStateRevisionDomain : byte {
    Inventory,
    Clothing,
    Furnace,
    Health,
    Damage,
    Time,
    Weather,
    Sky,
    Sleep,
    Fire,
    Mount,
    VitalStats,
    CreativeFly,
    PlayerProgression,
    PlayerStats,
    Flu,
    Sickness,
    CreatureChase,
    CreatureDigInMud,
    CreatureEatPickable,
    CreatureFishOutOfWater,
    CreatureLookAround,
    CreatureRandomFeed,
    CreatureRandomPeck,
    CreatureStubbornSteed,
    HumanRow
}

public readonly record struct NetworkStateRevisionMetrics(
    int AuthoritativeTrackedStates,
    int ClientTrackedStates,
    long AppliedStates,
    long IgnoredStaleStates,
    long IgnoredLifecycleStates);

internal readonly record struct NetworkStateRevisionDecision(
    bool Apply,
    bool Ignored,
    long Revision);

internal sealed class NetworkStateRevisionWindow {
    public bool HasState { get; private set; }

    public long AppliedRevision { get; private set; } = -1;

    public NetworkStateRevisionDecision Receive(long revision, bool forceRefresh = false) {
        if (revision < 0
            || HasState && revision < AppliedRevision
            || HasState && revision == AppliedRevision && !forceRefresh) {
            return new NetworkStateRevisionDecision(false, true, AppliedRevision);
        }

        HasState = true;
        AppliedRevision = revision;
        return new NetworkStateRevisionDecision(true, false, revision);
    }
}

public static class NetworkStateRevisionTracker {
    private static readonly ConditionalWeakTable<Project, RevisionContext> Contexts = new();

    public static long AdvanceAuthoritativeRevision(
        Project project,
        NetworkStateRevisionDomain domain,
        int stateId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateStateId(stateId);
        RevisionContext context = Context(project);
        StateKey key = new(domain, stateId);
        context.AuthoritativeRevisions.TryGetValue(key, out long current);
        long revision = checked(current + 1);
        context.AuthoritativeRevisions[key] = revision;
        return revision;
    }

    public static long GetAuthoritativeRevision(
        Project project,
        NetworkStateRevisionDomain domain,
        int stateId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateStateId(stateId);
        return Context(project).AuthoritativeRevisions.GetValueOrDefault(
            new StateKey(domain, stateId));
    }

    public static long GetAppliedRevision(
        Project project,
        NetworkStateRevisionDomain domain,
        int stateId) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateStateId(stateId);
        return Context(project).ClientWindows.TryGetValue(
                new StateKey(domain, stateId),
                out NetworkStateRevisionWindow window)
            && window.HasState
                ? window.AppliedRevision
                : -1;
    }

    internal static NetworkStateRevisionDecision Receive(
        Project project,
        NetworkStateRevisionDomain domain,
        int stateId,
        long revision,
        bool forceRefresh = false) {
        ArgumentNullException.ThrowIfNull(project);
        ValidateStateId(stateId);
        RevisionContext context = Context(project);
        StateKey key = new(domain, stateId);
        if (!context.ClientWindows.TryGetValue(key, out NetworkStateRevisionWindow window)) {
            window = new NetworkStateRevisionWindow();
            context.ClientWindows.Add(key, window);
        }
        NetworkStateRevisionDecision decision = window.Receive(revision, forceRefresh);
        if (decision.Apply) {
            context.AppliedStates++;
        }
        else if (decision.Ignored) {
            context.IgnoredStaleStates++;
        }
        return decision;
    }

    public static NetworkStateRevisionMetrics GetMetrics(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        RevisionContext context = Context(project);
        return new NetworkStateRevisionMetrics(
            context.AuthoritativeRevisions.Count,
            context.ClientWindows.Count,
            context.AppliedStates,
            context.IgnoredStaleStates,
            context.IgnoredLifecycleStates);
    }

    public static void RecordIgnoredLifecycleState(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        Context(project).IgnoredLifecycleStates++;
    }

    private static RevisionContext Context(Project project) =>
        Contexts.GetValue(project, _ => new RevisionContext());

    private static void ValidateStateId(int stateId) {
        if (stateId <= 0) {
            throw new ArgumentOutOfRangeException(nameof(stateId));
        }
    }

    private readonly record struct StateKey(
        NetworkStateRevisionDomain Domain,
        int StateId);

    private sealed class RevisionContext {
        public Dictionary<StateKey, long> AuthoritativeRevisions { get; } = [];
        public Dictionary<StateKey, NetworkStateRevisionWindow> ClientWindows { get; } = [];
        public long AppliedStates;
        public long IgnoredStaleStates;
        public long IgnoredLifecycleStates;
    }
}
