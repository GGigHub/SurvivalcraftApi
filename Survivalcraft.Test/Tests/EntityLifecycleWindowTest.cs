using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class EntityLifecycleWindowTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void StaleLoadCannotResurrectRemovedEntity() {
        EntityLifecycleWindow window = new();

        Assert.True(window.ReceiveLoad(1).Apply);
        Assert.True(window.ReceiveRemoval(2).Apply);
        EntityLifecycleDecision stale = window.ReceiveLoad(1);

        Assert.True(stale.Ignored);
        Assert.False(window.Present);
        Assert.Equal(2, window.AppliedRevision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void NewGenerationCanReplaceTombstone() {
        EntityLifecycleWindow window = new();

        Assert.True(window.ReceiveLoad(1).Apply);
        Assert.True(window.ReceiveRemoval(2).Apply);
        Assert.True(window.ReceiveLoad(3).Apply);

        Assert.True(window.Present);
        Assert.Equal(3, window.AppliedRevision);
        Assert.True(window.CanApplyState(3));
        Assert.False(window.CanApplyState(1));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DuplicatePacketsAreIdempotent() {
        EntityLifecycleWindow window = new();

        Assert.True(window.ReceiveLoad(4).Apply);
        Assert.True(window.ReceiveLoad(4).Ignored);
        Assert.True(window.ReceiveRemoval(5).Apply);
        Assert.True(window.ReceiveRemoval(5).Ignored);

        Assert.False(window.Present);
        Assert.Equal(5, window.AppliedRevision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EqualRevisionCanRefreshMissingMaterializedState() {
        EntityLifecycleWindow window = new();

        Assert.True(window.ReceiveLoad(8).Apply);
        Assert.True(window.ReceiveLoad(8, forceRefresh: true).Apply);
        Assert.True(window.ReceiveRemoval(9).Apply);
        Assert.True(window.ReceiveRemoval(9, forceRefresh: true).Apply);

        Assert.False(window.Present);
        Assert.Equal(9, window.AppliedRevision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void InvalidRevisionCannotCreateStateOrTriggerLoadRequest() {
        EntityLifecycleWindow window = new();

        Assert.True(window.ReceiveLoad(-1).Ignored);
        Assert.True(window.ReceiveRemoval(-1).Ignored);

        Assert.False(window.HasState);
        Assert.Equal(-1, window.AppliedRevision);
        Assert.False(window.ShouldRequestLoad(-1, entityExists: false, now: 0));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MissingEntityLoadRequestsUseBoundedExponentialRetry() {
        EntityLifecycleWindow window = new();

        Assert.True(window.ShouldRequestLoad(1, entityExists: false, now: 0));
        Assert.False(window.ShouldRequestLoad(1, entityExists: false, now: 0.249));
        Assert.True(window.ShouldRequestLoad(1, entityExists: false, now: 0.25));
        Assert.False(window.ShouldRequestLoad(1, entityExists: false, now: 0.749));
        Assert.True(window.ShouldRequestLoad(1, entityExists: false, now: 0.75));
        Assert.True(window.ShouldRequestLoad(2, entityExists: false, now: 0.751));

        Assert.True(window.ReceiveLoad(2).Apply);
        Assert.False(window.ShouldRequestLoad(2, entityExists: true, now: 10));
    }
}
