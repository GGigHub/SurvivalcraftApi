using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class CreativeFlyResultTrackerTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void ResultWindowIsBoundedAndFirstTerminalResultWins() {
        CreativeFlyResultTracker.Reset();
        for (long requestId = 1;
             requestId <= CreativeFlyResultTracker.MaximumRememberedResults + 1;
             requestId++) {
            Assert.True(CreativeFlyResultTracker.TryReserve(requestId));
            Assert.True(CreativeFlyResultTracker.Complete(
                new NetworkCreativeFlyResult(
                    requestId,
                    CreativeFlyResultCode.Accepted,
                    (int)requestId,
                    (int)requestId + 100,
                    Enabled: true,
                    Revision: requestId + 10)));
        }

        Assert.False(CreativeFlyResultTracker.TryTake(1, out _));
        long latestId = CreativeFlyResultTracker.MaximumRememberedResults + 1;
        Assert.False(CreativeFlyResultTracker.TryReserve(latestId));
        Assert.True(CreativeFlyResultTracker.TryTake(
            latestId,
            out NetworkCreativeFlyResult latest));
        Assert.Equal(CreativeFlyResultCode.Accepted, latest.Code);
        Assert.True(latest.Enabled);
        Assert.Equal(latestId + 10, latest.Revision);
        CreativeFlyResultTracker.Reset();
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void UnreservedAndDuplicateResultsCannotReplaceTheFirstResult() {
        CreativeFlyResultTracker.Reset();
        Assert.False(CreativeFlyResultTracker.Complete(
            new NetworkCreativeFlyResult(
                41,
                CreativeFlyResultCode.Accepted,
                1,
                2,
                Enabled: true,
                Revision: 3)));
        Assert.True(CreativeFlyResultTracker.TryReserve(41));
        Assert.True(CreativeFlyResultTracker.Complete(
            new NetworkCreativeFlyResult(
                41,
                CreativeFlyResultCode.InvalidGameMode,
                1,
                2,
                Enabled: false,
                Revision: 3)));
        Assert.False(CreativeFlyResultTracker.Complete(
            new NetworkCreativeFlyResult(
                41,
                CreativeFlyResultCode.Accepted,
                1,
                2,
                Enabled: true,
                Revision: 4)));
        Assert.True(CreativeFlyResultTracker.TryTake(41, out NetworkCreativeFlyResult result));
        Assert.Equal(CreativeFlyResultCode.InvalidGameMode, result.Code);
        Assert.False(result.Enabled);
        Assert.False(CreativeFlyResultTracker.Complete(
            new NetworkCreativeFlyResult(
                41,
                CreativeFlyResultCode.Accepted,
                1,
                2,
                Enabled: true,
                Revision: 5)));
        Assert.False(CreativeFlyResultTracker.TryTake(41, out _));
        CreativeFlyResultTracker.Reset();
    }
}
