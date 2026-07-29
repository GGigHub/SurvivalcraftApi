using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class NetworkStateRevisionWindowTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void NewerStateWinsAndOlderOrDuplicateStateIsIgnored() {
        NetworkStateRevisionWindow window = new();

        NetworkStateRevisionDecision first = window.Receive(4);
        NetworkStateRevisionDecision duplicate = window.Receive(4);
        NetworkStateRevisionDecision stale = window.Receive(3);
        NetworkStateRevisionDecision newer = window.Receive(5);

        Assert.True(first.Apply);
        Assert.False(duplicate.Apply);
        Assert.True(duplicate.Ignored);
        Assert.False(stale.Apply);
        Assert.True(stale.Ignored);
        Assert.True(newer.Apply);
        Assert.Equal(5, window.AppliedRevision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FullSnapshotCanRefreshSameRevisionWithoutAllowingRollback() {
        NetworkStateRevisionWindow window = new();
        Assert.True(window.Receive(12).Apply);

        NetworkStateRevisionDecision refresh = window.Receive(12, forceRefresh: true);
        NetworkStateRevisionDecision stale = window.Receive(11, forceRefresh: true);

        Assert.True(refresh.Apply);
        Assert.False(stale.Apply);
        Assert.Equal(12, window.AppliedRevision);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [Trait("Layer", "L0")]
    public void NegativeRevisionIsRejected(int revision) {
        NetworkStateRevisionWindow window = new();

        NetworkStateRevisionDecision decision = window.Receive(revision);

        Assert.False(decision.Apply);
        Assert.True(decision.Ignored);
        Assert.False(window.HasState);
    }
}
