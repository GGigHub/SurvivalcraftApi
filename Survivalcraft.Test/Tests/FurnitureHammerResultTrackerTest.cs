using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class FurnitureHammerResultTrackerTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void ResultWindowIsBoundedAndRequestIdentityCannotBeReservedTwice() {
        FurnitureHammerResultTracker.Reset();
        for (long requestId = 1;
             requestId <= FurnitureHammerResultTracker.MaximumRememberedResults + 1;
             requestId++) {
            Assert.True(FurnitureHammerResultTracker.TryReserve(requestId));
            Assert.True(FurnitureHammerResultTracker.Complete(
                new NetworkFurnitureHammerResult(
                    requestId,
                    FurnitureHammerResultCode.Accepted,
                    (int)requestId,
                    227,
                    4,
                    string.Empty)));
        }

        Assert.False(FurnitureHammerResultTracker.TryTake(1, out _));
        long latestId = FurnitureHammerResultTracker.MaximumRememberedResults + 1;
        Assert.False(FurnitureHammerResultTracker.TryReserve(latestId));
        Assert.True(FurnitureHammerResultTracker.TryTake(
            latestId,
            out NetworkFurnitureHammerResult latest));
        Assert.Equal(FurnitureHammerResultCode.Accepted, latest.Code);
        Assert.Equal(latestId, latest.DesignIndex);
        Assert.False(FurnitureHammerResultTracker.TryTake(latestId, out _));
        FurnitureHammerResultTracker.Reset();
    }
}
