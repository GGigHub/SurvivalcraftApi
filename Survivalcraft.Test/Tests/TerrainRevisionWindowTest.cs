using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class TerrainRevisionWindowTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void OutOfOrderChangesDrainAfterBaselineInRevisionOrder() {
        TerrainRevisionWindow<string> window = new();

        TerrainRevisionApplyPlan<string> second = window.ReceiveChange(2, "two");
        TerrainRevisionApplyPlan<string> first = window.ReceiveChange(1, "one");
        TerrainRevisionApplyPlan<string> baseline = window.ReceiveSnapshot(0);

        Assert.True(second.Buffered);
        Assert.True(first.Buffered);
        Assert.True(baseline.ApplySnapshot);
        Assert.True(baseline.Changes.SequenceEqual(["one", "two"]));
        Assert.Equal(2, window.AppliedRevision);
        Assert.Equal(0, window.PendingCount);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DuplicateAndStalePacketsCannotRegressAppliedRevision() {
        TerrainRevisionWindow<string> window = new();

        Assert.True(window.ReceiveSnapshot(4).ApplySnapshot);
        Assert.True(window.ReceiveChange(5, "new").Changes.SequenceEqual(["new"]));
        Assert.True(window.ReceiveChange(5, "duplicate").Ignored);
        Assert.True(window.ReceiveSnapshot(3).Ignored);

        Assert.Equal(5, window.AppliedRevision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void NewerSnapshotSupersedesGapsAndEqualSnapshotCanRefreshFreedChunk() {
        TerrainRevisionWindow<string> window = new();

        Assert.True(window.ReceiveSnapshot(0).ApplySnapshot);
        Assert.True(window.ReceiveChange(3, "obsolete-gap").Buffered);
        TerrainRevisionApplyPlan<string> newer = window.ReceiveSnapshot(5);
        TerrainRevisionApplyPlan<string> equalRefresh = window.ReceiveSnapshot(5, forceRefresh: true);

        Assert.True(newer.ApplySnapshot);
        Assert.Empty(newer.Changes);
        Assert.True(equalRefresh.ApplySnapshot);
        Assert.Equal(5, window.AppliedRevision);
        Assert.Equal(0, window.PendingCount);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PendingWindowIsBounded() {
        TerrainRevisionWindow<string> window = new(maximumPendingChanges: 2);

        Assert.True(window.ReceiveChange(10, "ten").Buffered);
        Assert.True(window.ReceiveChange(11, "eleven").Buffered);
        TerrainRevisionApplyPlan<string> overflow = window.ReceiveChange(12, "twelve");

        Assert.True(overflow.Ignored);
        Assert.True(overflow.Overflowed);
        Assert.Equal(2, window.PendingCount);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void InvalidRevisionsCannotEstablishOrAdvanceBaseline() {
        TerrainRevisionWindow<string> window = new();

        Assert.True(window.ReceiveSnapshot(-1).Ignored);
        Assert.True(window.ReceiveChange(0, "zero").Ignored);

        Assert.False(window.HasBaseline);
        Assert.Equal(-1, window.AppliedRevision);
        Assert.Equal(0, window.PendingCount);
    }
}
