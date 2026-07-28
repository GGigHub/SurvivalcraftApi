using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class PickableSyncTest : IClassFixture<GameFixture> {

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public PickableSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestPickableCreateSync() {
        await GameTools.CreateTestWorld(server);
        await PickableSyncTools.ConnectTestServerForPickableTest(client, server.GamePort);

        int pickableId = await PickableSyncTools.CreatePickableAsync(server, 7, 4f, 2f, 4f);
        Assert.True(pickableId > 0, "Server did not create a pickable.");

        PickableState serverState = await PickableSyncTools.GetPickableStateAsync(server, pickableId);
        PickableState clientState = await PickableSyncTools.WaitForPickableStateAsync(client, pickableId);

        Assert.Equal(serverState.Value, clientState.Value);
        Assert.Equal(serverState.Count, clientState.Count);
        Assert.True(PickableSyncTools.PositionsMatch(serverState, clientState), $"Created pickable position mismatch. Server={serverState}, Client={clientState}.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestPickableDeleteSync() {
        int pickableId = await PickableSyncTools.CreatePickableAsync(server, 3, 5f, 2f, 4f);
        Assert.True(await PickableSyncTools.WaitForPickableExistsAsync(client, pickableId), $"Client did not receive pickable {pickableId} before deletion.");

        int removedId = await PickableSyncTools.RemovePickableAsync(server, pickableId);
        Assert.Equal(pickableId, removedId);

        Assert.True(await PickableSyncTools.WaitForPickableMissingAsync(client, pickableId), $"Client still has removed pickable {pickableId}.");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestPickableUpdateSyncIsAutomaticAndIncremental() {
        int movingId = await PickableSyncTools.CreatePickableAsync(server, 1, 6f, 2f, 4f);
        int unchangedId = await PickableSyncTools.CreatePickableAsync(server, 1, 8f, 2f, 4f);
        (_, PickableState initialMovingClientState) =
            await PickableSyncTools.WaitForPickableStatesMatchAsync(server, client, movingId);
        Assert.True(await PickableSyncTools.WaitForPickableExistsAsync(client, unchangedId), $"Client did not receive unchanged pickable {unchangedId}.");

        PickableState forcedServerState = await PickableSyncTools.ForcePickablePositionAsync(server, movingId, 12f, 4f, 12f);

        (PickableState finalServerState, PickableState finalClientState) = await TestWaiter.UntilAsync(
            async () => (
                ServerState: await PickableSyncTools.GetPickableStateAsync(server, movingId),
                ClientState: await PickableSyncTools.GetPickableStateAsync(client, movingId)),
            states => states.ClientState.Exists
                && PickableVector3Dto.DistanceSquared(initialMovingClientState.Position, states.ClientState.Position) > 16f
                && PickableSyncTools.PositionsMatch(states.ServerState, states.ClientState),
            TestTimeouts.DefaultEventually,
            $"Client did not receive automatic pickable update. Forced={forcedServerState}.");
        Assert.True(
            await PickableSyncTools.WaitForPickableExistsAsync(client, unchangedId),
            "Incremental removed a pickable that was not present in the update payload.");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestPickableSetFlyToPositionSync() {
        PickableState createdState = await PickableSyncTools.CreateAttractablePickableAsync(server);
        Assert.True(createdState.Exists, $"Server did not create an attractable pickable. State={createdState}.");
        await PickableSyncTools.WaitForPickableStatesMatchAsync(server, client, createdState.Id);
        await PickableSyncTools.DelayPickableAutoPickupAsync(client, createdState.Id);

        PickableState serverState = await PickableSyncTools.AttractPickableAsync(server, createdState.Id);
        Assert.True(serverState.Exists && serverState.FlyToPosition.HasValue, $"Server did not attract the pickable. State={serverState}.");

        PickableState clientState = await TestWaiter.UntilAsync(
            () => PickableSyncTools.GetPickableStateAsync(client, serverState.Id),
            state => state.Exists
                && state.FlyToPosition.HasValue
                && PickableVector3Dto.DistanceSquared(serverState.FlyToPosition.Value, state.FlyToPosition.Value) <= PickableSyncTools.SyncedPositionEpsilonSquared,
            TestTimeouts.DefaultEventually,
            $"Client did not receive PickableSetFlyToPosition sync. Server={serverState}.");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
