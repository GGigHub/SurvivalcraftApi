using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class NetworkMutationRequestTrackerTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void DuplicateIdentityIsScopedBySessionAndMutationDomain() {
        ClientSession firstSession = new();
        ClientSession secondSession = new();
        const long requestId = 1_000_000_007L;

        Assert.True(NetworkMutationRequestTracker.TryAccept(
            firstSession,
            NetworkMutationDomain.InventoryMove,
            requestId));
        Assert.False(NetworkMutationRequestTracker.TryAccept(
            firstSession,
            NetworkMutationDomain.InventoryMove,
            requestId));
        Assert.True(NetworkMutationRequestTracker.TryAccept(
            firstSession,
            NetworkMutationDomain.PlayerDrop,
            requestId));
        Assert.True(NetworkMutationRequestTracker.TryAccept(
            secondSession,
            NetworkMutationDomain.InventoryMove,
            requestId));
        Assert.False(NetworkMutationRequestTracker.TryAccept(
            null!,
            NetworkMutationDomain.InventoryMove,
            requestId));
        Assert.False(NetworkMutationRequestTracker.TryAccept(
            firstSession,
            NetworkMutationDomain.InventoryMove,
            0));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task ConcurrentDuplicateDeliveryExecutesExactlyOnce() {
        ClientSession session = new();
        long requestId = NetworkMutationRequestTracker.CreateRequestId();
        Task<bool>[] attempts = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => NetworkMutationRequestTracker.TryAccept(
                session,
                NetworkMutationDomain.PlayerHit,
                requestId)))
            .ToArray();

        bool[] accepted = await Task.WhenAll(attempts);

        Assert.Single(accepted, value => value);
        long nextRequestId = NetworkMutationRequestTracker.CreateRequestId();
        Assert.True(nextRequestId > requestId);
    }
}
