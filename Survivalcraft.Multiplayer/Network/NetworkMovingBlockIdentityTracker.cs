using System.Runtime.CompilerServices;
using System.Threading;
using Engine;
using GameEntitySystem;

namespace Game.Network;

/// <summary>
/// Gives every moving-block set a wire-stable instance identity. Tagged sets
/// continue to share one logical lifecycle key so a replacement advances its
/// generation, while untagged sets use the instance ID to coexist safely.
/// </summary>
internal static class NetworkMovingBlockIdentityTracker {
    private static long s_nextInstanceId;
    private static readonly ConditionalWeakTable<
        SubsystemMovingBlocks.MovingBlockSet,
        InstanceIdentity> Identities = new();
    private static readonly ConditionalWeakTable<Project, ProjectContext> Projects = new();

    internal static long GetOrCreate(
        SubsystemMovingBlocks.MovingBlockSet movingSet) {
        ArgumentNullException.ThrowIfNull(movingSet);
        return Identities.GetValue(
            movingSet,
            _ => new InstanceIdentity(Interlocked.Increment(ref s_nextInstanceId))).Value;
    }

    internal static void Register(
        Project project,
        SubsystemMovingBlocks.MovingBlockSet movingSet,
        long instanceId) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(movingSet);
        if (instanceId <= 0) {
            throw new ArgumentOutOfRangeException(nameof(instanceId));
        }
        if (Identities.TryGetValue(movingSet, out InstanceIdentity existing)) {
            if (existing.Value != instanceId) {
                throw new InvalidOperationException(
                    "Moving-block set already has a different network instance ID.");
            }
        }
        else {
            Identities.Add(movingSet, new InstanceIdentity(instanceId));
        }
        ProjectContext context = Projects.GetValue(project, _ => new ProjectContext());
        lock (context.SyncRoot) {
            context.Sets[instanceId] = movingSet;
        }
    }

    internal static bool TryGet(
        Project project,
        long instanceId,
        out SubsystemMovingBlocks.MovingBlockSet movingSet) {
        ArgumentNullException.ThrowIfNull(project);
        movingSet = null;
        if (instanceId <= 0
            || !Projects.TryGetValue(project, out ProjectContext context)) {
            return false;
        }
        lock (context.SyncRoot) {
            return context.Sets.TryGetValue(instanceId, out movingSet);
        }
    }

    internal static bool IsRegistered(
        Project project,
        SubsystemMovingBlocks.MovingBlockSet movingSet) {
        ArgumentNullException.ThrowIfNull(project);
        if (movingSet == null
            || !Identities.TryGetValue(movingSet, out InstanceIdentity identity)
            || !Projects.TryGetValue(project, out ProjectContext context)) {
            return false;
        }
        lock (context.SyncRoot) {
            return context.Sets.TryGetValue(
                    identity.Value,
                    out SubsystemMovingBlocks.MovingBlockSet current)
                && ReferenceEquals(current, movingSet);
        }
    }

    internal static void Unregister(
        Project project,
        SubsystemMovingBlocks.MovingBlockSet movingSet) {
        ArgumentNullException.ThrowIfNull(project);
        if (movingSet == null
            || !Identities.TryGetValue(movingSet, out InstanceIdentity identity)
            || !Projects.TryGetValue(project, out ProjectContext context)) {
            return;
        }
        lock (context.SyncRoot) {
            if (context.Sets.TryGetValue(identity.Value, out SubsystemMovingBlocks.MovingBlockSet current)
                && ReferenceEquals(current, movingSet)) {
                context.Sets.Remove(identity.Value);
            }
        }
    }

    internal static NetworkLogicStateKey CreateStateKey(
        string identity,
        bool hasTag,
        Point3 tag,
        long instanceId) =>
        NetworkLogicStateKey.MovingBlock(identity, hasTag, tag, instanceId);

    internal static NetworkLogicStateKey CreateStateKey(
        SubsystemMovingBlocks.MovingBlockSet movingSet) {
        ArgumentNullException.ThrowIfNull(movingSet);
        bool hasTag = movingSet.Tag is Point3;
        return CreateStateKey(
            movingSet.Id,
            hasTag,
            hasTag ? (Point3)movingSet.Tag : default,
            GetOrCreate(movingSet));
    }

    private sealed record InstanceIdentity(long Value);

    private sealed class ProjectContext {
        internal object SyncRoot { get; } = new();
        internal Dictionary<long, SubsystemMovingBlocks.MovingBlockSet> Sets { get; } = [];
    }
}
