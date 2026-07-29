using Engine;
using Game.Network;
using GameEntitySystem;
using Xunit;

namespace Survivalcraft.Test;

public sealed class NetworkLogicStateTrackerTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void EditableStateRejectsDuplicateAndRollback() {
        using Project project = new();
        NetworkLogicStateKey key = NetworkLogicStateKey.Editable(
            NetworkLogicElementKind.Battery,
            isItem: false,
            inventoryId: 0,
            slotIndex: 0,
            new Point3(1, 70, 2));

        Assert.True(NetworkLogicStateTracker.Receive(project, key, 0).Apply);
        Assert.False(NetworkLogicStateTracker.Receive(project, key, 0).Apply);
        Assert.True(NetworkLogicStateTracker.Receive(project, key, 2).Apply);
        Assert.False(NetworkLogicStateTracker.Receive(project, key, 1).Apply);

        NetworkLogicStateMetrics metrics = NetworkLogicStateTracker.GetMetrics(project);
        Assert.Equal(2, metrics.AppliedStates);
        Assert.Equal(2, metrics.IgnoredStaleStates);
        Assert.Equal(2, NetworkLogicStateTracker.GetAppliedRevision(project, key));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MovingTombstoneRequiresANewerGenerationToReappear() {
        using Project project = new();
        NetworkLogicStateKey key = NetworkLogicStateKey.MovingBlock(
            "Piston",
            hasTag: true,
            new Point3(4, 71, 6));

        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 1, revision: 1, present: true).Apply);
        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 1, revision: 2, present: false).Apply);

        NetworkLogicStateDecision delayedAdd = NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 1, revision: 3, present: true);
        NetworkLogicStateDecision replacement = NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 2, revision: 1, present: true);
        NetworkLogicStateDecision oldRemove = NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 1, revision: 99, present: false);

        Assert.False(delayedAdd.Apply);
        Assert.True(delayedAdd.LifecycleMismatch);
        Assert.True(replacement.Apply);
        Assert.False(oldRemove.Apply);
        Assert.True(oldRemove.LifecycleMismatch);
        Assert.True(NetworkLogicStateTracker.IsMovingPresent(project, key));
        Assert.Equal(2, NetworkLogicStateTracker.GetAppliedMovingGeneration(project, key));

        NetworkLogicStateMetrics metrics = NetworkLogicStateTracker.GetMetrics(project);
        Assert.Equal(3, metrics.AppliedStates);
        Assert.Equal(2, metrics.IgnoredLifecycleStates);
        Assert.Equal(0, metrics.MovingTombstones);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ServerGenerationAndRevisionResetForReplacementLifecycle() {
        using Project project = new();
        NetworkLogicStateKey key = NetworkLogicStateKey.MovingBlock(
            "Elevator",
            hasTag: false,
            default,
            instanceId: 41);

        Assert.Equal(1, NetworkLogicStateTracker.AdvanceAuthoritativeMovingGeneration(project, key));
        Assert.Equal(1, NetworkLogicStateTracker.AdvanceAuthoritativeRevision(project, key));
        Assert.Equal(2, NetworkLogicStateTracker.AdvanceAuthoritativeRevision(project, key));
        Assert.Equal(2, NetworkLogicStateTracker.AdvanceAuthoritativeMovingGeneration(project, key));
        Assert.Equal(0, NetworkLogicStateTracker.GetAuthoritativeRevision(project, key));
        Assert.Equal(1, NetworkLogicStateTracker.AdvanceAuthoritativeRevision(project, key));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void UntaggedMovingSetsHaveIndependentInstanceLifecycles() {
        using Project project = new();
        NetworkLogicStateKey first = NetworkLogicStateKey.MovingBlock(
            "CollapsingBlock", false, default, instanceId: 101);
        NetworkLogicStateKey second = NetworkLogicStateKey.MovingBlock(
            "CollapsingBlock", false, default, instanceId: 102);

        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project, first, generation: 1, revision: 1, present: true).Apply);
        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project, second, generation: 1, revision: 1, present: true).Apply);
        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project, first, generation: 1, revision: 2, present: false).Apply);

        Assert.False(NetworkLogicStateTracker.IsMovingPresent(project, first));
        Assert.True(NetworkLogicStateTracker.IsMovingPresent(project, second));
        Assert.Equal(1, NetworkLogicStateTracker.GetMetrics(project).MovingTombstones);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EqualMovingStateCanRefreshMissingMaterializedState() {
        using Project project = new();
        NetworkLogicStateKey key = NetworkLogicStateKey.MovingBlock(
            "Elevator", false, default, instanceId: 301);

        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 4, revision: 7, present: true).Apply);
        Assert.False(NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 4, revision: 7, present: true).Apply);
        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project,
            key,
            generation: 4,
            revision: 7,
            present: true,
            forceRefresh: true).Apply);
        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 4, revision: 8, present: false).Apply);
        Assert.False(NetworkLogicStateTracker.ReceiveMoving(
            project, key, generation: 4, revision: 8, present: false).Apply);
        Assert.True(NetworkLogicStateTracker.ReceiveMoving(
            project,
            key,
            generation: 4,
            revision: 8,
            present: false,
            forceRefresh: true).Apply);

        NetworkLogicStateMetrics metrics = NetworkLogicStateTracker.GetMetrics(project);
        Assert.Equal(4, metrics.AppliedStates);
        Assert.Equal(2, metrics.IgnoredStaleStates);
        Assert.Equal(1, metrics.MovingTombstones);
    }
}
