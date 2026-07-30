using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class ContainerOpenResultTrackerTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void ResultWindowIsBoundedAndLateDuplicatesCannotRepeatSideEffects() {
        ContainerOpenResultTracker.Reset();
        for (long requestId = 1;
             requestId <= ContainerOpenResultTracker.MaximumRememberedResults + 1;
             requestId++) {
            Assert.True(ContainerOpenResultTracker.Record(new NetworkContainerOpenResult(
                requestId,
                NetworkContainerOpenRequestKind.ById,
                NetworkContainerOpenResultCode.Accepted,
                (int)requestId,
                Opened: true)));
        }
        Assert.False(ContainerOpenResultTracker.Record(new NetworkContainerOpenResult(
            ContainerOpenResultTracker.MaximumRememberedResults + 1,
            NetworkContainerOpenRequestKind.ById,
            NetworkContainerOpenResultCode.PermissionDenied,
            999,
            Opened: false)));

        Assert.False(ContainerOpenResultTracker.TryTake(1, out _));
        Assert.True(ContainerOpenResultTracker.TryTake(
            ContainerOpenResultTracker.MaximumRememberedResults + 1,
            out NetworkContainerOpenResult latest));
        Assert.Equal(NetworkContainerOpenResultCode.Accepted, latest.Code);
        Assert.Equal(ContainerOpenResultTracker.MaximumRememberedResults + 1, latest.InventoryId);
        Assert.True(latest.Opened);
        Assert.False(ContainerOpenResultTracker.Record(new NetworkContainerOpenResult(
            ContainerOpenResultTracker.MaximumRememberedResults + 1,
            NetworkContainerOpenRequestKind.ById,
            NetworkContainerOpenResultCode.Accepted,
            999,
            Opened: true)));
        Assert.False(ContainerOpenResultTracker.TryTake(
            ContainerOpenResultTracker.MaximumRememberedResults + 1,
            out _));
        ContainerOpenResultTracker.Reset();
    }
}
