using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;

namespace Game.Network;

public enum NetworkLogicStateDomain : byte {
    Editable,
    Electricity,
    MovingBlock
}

public enum NetworkLogicElementKind : byte {
    None,
    AdjustableDelayGate,
    Battery,
    Button,
    Switch,
    Piston,
    MemoryBank,
    TruthTable,
    Sign,
    Dispenser
}

public readonly record struct NetworkLogicStateKey(
    NetworkLogicStateDomain Domain,
    NetworkLogicElementKind ElementKind,
    bool IsItem,
    int InventoryId,
    int SlotIndex,
    Point3 Point,
    string Identity,
    bool HasTag,
    Point3 Tag,
    long InstanceId) {

    public static NetworkLogicStateKey Editable(
        NetworkLogicElementKind kind,
        bool isItem,
        int inventoryId,
        int slotIndex,
        Point3 point) =>
        new(
            NetworkLogicStateDomain.Editable,
            kind,
            isItem,
            isItem ? inventoryId : 0,
            isItem ? slotIndex : 0,
            isItem ? default : point,
            string.Empty,
            false,
            default,
            0);

    public static NetworkLogicStateKey Electricity() =>
        new(
            NetworkLogicStateDomain.Electricity,
            NetworkLogicElementKind.None,
            false,
            0,
            0,
            default,
            string.Empty,
            false,
            default,
            0);

    public static NetworkLogicStateKey MovingBlock(
        string identity,
        bool hasTag,
        Point3 tag,
        long instanceId = 0) =>
        new(
            NetworkLogicStateDomain.MovingBlock,
            NetworkLogicElementKind.None,
            false,
            0,
            0,
            default,
            identity ?? string.Empty,
            hasTag,
            hasTag ? tag : default,
            hasTag ? 0 : instanceId);
}

public readonly record struct NetworkLogicStateMetrics(
    int AuthoritativeTrackedStates,
    int ClientTrackedStates,
    long AppliedStates,
    long IgnoredStaleStates,
    long IgnoredLifecycleStates,
    int AuthoritativeMovingGenerations,
    int ClientMovingGenerations,
    int MovingTombstones);

public readonly record struct NetworkLogicAuthoritativeState(
    NetworkLogicStateKey Key,
    long Revision,
    long Generation);

internal readonly record struct NetworkLogicStateDecision(
    bool Apply,
    bool Ignored,
    bool LifecycleMismatch,
    long Revision,
    long Generation,
    bool Present);

/// <summary>
/// Tracks authoritative logic revisions independently from terrain chunks and
/// gives moving-block identities an explicit generation/tombstone lifecycle.
/// A project-scoped context prevents revisions from leaking across save reloads.
/// </summary>
public static class NetworkLogicStateTracker {
    private static readonly ConditionalWeakTable<Project, RevisionContext> Contexts = new();

    public static long AdvanceAuthoritativeRevision(
        Project project,
        NetworkLogicStateKey key) {
        Validate(project, key);
        RevisionContext context = Context(project);
        context.AuthoritativeRevisions.TryGetValue(key, out long current);
        long revision = checked(current + 1);
        context.AuthoritativeRevisions[key] = revision;
        return revision;
    }

    public static long GetAuthoritativeRevision(
        Project project,
        NetworkLogicStateKey key) {
        Validate(project, key);
        return Context(project).AuthoritativeRevisions.GetValueOrDefault(key);
    }

    public static long GetAppliedRevision(
        Project project,
        NetworkLogicStateKey key) {
        Validate(project, key);
        return Context(project).ClientStates.TryGetValue(key, out ClientState state)
            && state.HasState
                ? state.Revision
                : 0;
    }

    public static long AdvanceAuthoritativeMovingGeneration(
        Project project,
        NetworkLogicStateKey key) {
        ValidateMoving(project, key);
        RevisionContext context = Context(project);
        context.AuthoritativeMovingGenerations.TryGetValue(key, out long current);
        long generation = checked(current + 1);
        context.AuthoritativeMovingGenerations[key] = generation;
        context.AuthoritativeRevisions[key] = 0;
        return generation;
    }

    public static long EnsureAuthoritativeMovingGeneration(
        Project project,
        NetworkLogicStateKey key) {
        ValidateMoving(project, key);
        RevisionContext context = Context(project);
        if (!context.AuthoritativeMovingGenerations.TryGetValue(key, out long generation)
            || generation <= 0) {
            generation = 1;
            context.AuthoritativeMovingGenerations[key] = generation;
        }
        return generation;
    }

    public static long GetAuthoritativeMovingGeneration(
        Project project,
        NetworkLogicStateKey key) {
        ValidateMoving(project, key);
        return Context(project).AuthoritativeMovingGenerations.GetValueOrDefault(key);
    }

    public static long GetAppliedMovingGeneration(
        Project project,
        NetworkLogicStateKey key) {
        ValidateMoving(project, key);
        return Context(project).ClientStates.TryGetValue(key, out ClientState state)
            && state.HasState
                ? state.Generation
                : 0;
    }

    public static bool IsMovingPresent(
        Project project,
        NetworkLogicStateKey key) {
        ValidateMoving(project, key);
        return Context(project).ClientStates.TryGetValue(key, out ClientState state)
            && state.HasState
            && state.Present;
    }

    internal static NetworkLogicStateDecision Receive(
        Project project,
        NetworkLogicStateKey key,
        long revision,
        bool forceRefresh = false) {
        Validate(project, key);
        if (key.Domain == NetworkLogicStateDomain.MovingBlock) {
            throw new ArgumentException(
                "Moving-block states must include a generation.",
                nameof(key));
        }
        RevisionContext context = Context(project);
        ClientState state = GetOrCreateClientState(context, key);
        if (revision < 0
            || state.HasState && revision < state.Revision
            || state.HasState && revision == state.Revision && !forceRefresh) {
            context.IgnoredStaleStates++;
            return new NetworkLogicStateDecision(
                false,
                true,
                false,
                state.Revision,
                state.Generation,
                state.Present);
        }
        state.HasState = true;
        state.Revision = revision;
        state.Generation = 0;
        state.Present = true;
        context.AppliedStates++;
        return new NetworkLogicStateDecision(
            true,
            false,
            false,
            revision,
            0,
            true);
    }

    internal static NetworkLogicStateDecision ReceiveMoving(
        Project project,
        NetworkLogicStateKey key,
        long generation,
        long revision,
        bool present,
        bool forceRefresh = false) {
        ValidateMoving(project, key);
        RevisionContext context = Context(project);
        ClientState state = GetOrCreateClientState(context, key);
        if (generation <= 0) {
            context.IgnoredLifecycleStates++;
            return IgnoredMoving(state, lifecycleMismatch: true);
        }
        if (state.HasState && generation < state.Generation) {
            context.IgnoredLifecycleStates++;
            return IgnoredMoving(state, lifecycleMismatch: true);
        }
        if (state.HasState
            && generation == state.Generation
            && !state.Present
            && present) {
            context.IgnoredLifecycleStates++;
            return IgnoredMoving(state, lifecycleMismatch: true);
        }
        if (revision < 0
            || state.HasState
                && generation == state.Generation
                && (revision < state.Revision
                    || revision == state.Revision && !forceRefresh)) {
            context.IgnoredStaleStates++;
            return IgnoredMoving(state, lifecycleMismatch: false);
        }
        state.HasState = true;
        state.Generation = generation;
        state.Revision = revision;
        state.Present = present;
        context.AppliedStates++;
        return new NetworkLogicStateDecision(
            true,
            false,
            false,
            revision,
            generation,
            present);
    }

    public static NetworkLogicStateMetrics GetMetrics(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        RevisionContext context = Context(project);
        return new NetworkLogicStateMetrics(
            context.AuthoritativeRevisions.Count,
            context.ClientStates.Count,
            context.AppliedStates,
            context.IgnoredStaleStates,
            context.IgnoredLifecycleStates,
            context.AuthoritativeMovingGenerations.Count,
            context.ClientStates.Count(pair =>
                pair.Key.Domain == NetworkLogicStateDomain.MovingBlock
                && pair.Value.HasState),
            context.ClientStates.Count(pair =>
                pair.Key.Domain == NetworkLogicStateDomain.MovingBlock
                && pair.Value.HasState
                && !pair.Value.Present));
    }

    public static NetworkLogicAuthoritativeState[] GetAuthoritativeStates(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        RevisionContext context = Context(project);
        return context.AuthoritativeRevisions
            .Select(pair => new NetworkLogicAuthoritativeState(
                pair.Key,
                pair.Value,
                context.AuthoritativeMovingGenerations.GetValueOrDefault(pair.Key)))
            .OrderBy(state => state.Key.Domain)
            .ThenBy(state => state.Key.ElementKind)
            .ThenBy(state => state.Key.IsItem)
            .ThenBy(state => state.Key.InventoryId)
            .ThenBy(state => state.Key.SlotIndex)
            .ThenBy(state => state.Key.Point.X)
            .ThenBy(state => state.Key.Point.Y)
            .ThenBy(state => state.Key.Point.Z)
            .ThenBy(state => state.Key.Identity, StringComparer.Ordinal)
            .ThenBy(state => state.Key.InstanceId)
            .ToArray();
    }

    private static NetworkLogicStateDecision IgnoredMoving(
        ClientState state,
        bool lifecycleMismatch) =>
        new(
            false,
            true,
            lifecycleMismatch,
            state.Revision,
            state.Generation,
            state.Present);

    private static RevisionContext Context(Project project) =>
        Contexts.GetValue(project, _ => new RevisionContext());

    private static ClientState GetOrCreateClientState(
        RevisionContext context,
        NetworkLogicStateKey key) {
        if (!context.ClientStates.TryGetValue(key, out ClientState state)) {
            state = new ClientState();
            context.ClientStates.Add(key, state);
        }
        return state;
    }

    private static void Validate(Project project, NetworkLogicStateKey key) {
        ArgumentNullException.ThrowIfNull(project);
        if (!Enum.IsDefined(key.Domain)
            || !Enum.IsDefined(key.ElementKind)) {
            throw new ArgumentOutOfRangeException(nameof(key));
        }
        if (key.Domain == NetworkLogicStateDomain.Editable
            && key.ElementKind == NetworkLogicElementKind.None) {
            throw new ArgumentException(
                "Editable logic keys require an element kind.",
                nameof(key));
        }
    }

    private static void ValidateMoving(
        Project project,
        NetworkLogicStateKey key) {
        Validate(project, key);
        if (key.Domain != NetworkLogicStateDomain.MovingBlock
            || string.IsNullOrWhiteSpace(key.Identity)
            || !key.HasTag && key.InstanceId <= 0) {
            throw new ArgumentException(
                "Moving-block keys require a non-empty identity and untagged instances require a positive ID.",
                nameof(key));
        }
    }

    private sealed class ClientState {
        public bool HasState;
        public long Revision;
        public long Generation;
        public bool Present;
    }

    private sealed class RevisionContext {
        public Dictionary<NetworkLogicStateKey, long> AuthoritativeRevisions { get; } = [];
        public Dictionary<NetworkLogicStateKey, long> AuthoritativeMovingGenerations { get; } = [];
        public Dictionary<NetworkLogicStateKey, ClientState> ClientStates { get; } = [];
        public long AppliedStates;
        public long IgnoredStaleStates;
        public long IgnoredLifecycleStates;
    }
}
