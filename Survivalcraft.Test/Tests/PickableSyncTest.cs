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
        PickableState initialMovingClientState = await PickableSyncTools.WaitForPickableStateAsync(client, movingId);
        Assert.True(await PickableSyncTools.WaitForPickableExistsAsync(client, unchangedId), $"Client did not receive unchanged pickable {unchangedId}.");

        await Task.Delay(300);
        PickableState forcedServerState = await PickableSyncTools.ForcePickablePositionAsync(server, movingId, 12f, 4f, 12f);

        PickableState finalServerState = default;
        PickableState finalClientState = default;
        bool synced = false;
        for (int i = 0; i < 50; i++) {
            await Task.Delay(100);
            finalServerState = await PickableSyncTools.GetPickableStateAsync(server, movingId);
            finalClientState = await PickableSyncTools.GetPickableStateAsync(client, movingId);
            if (finalClientState.Exists
                && Vector3Dto.DistanceSquared(initialMovingClientState.Position, finalClientState.Position) > 16f
                && PickableSyncTools.PositionsMatch(finalServerState, finalClientState)) {
                synced = true;
                break;
            }
        }

        Assert.True(
            synced,
            $"Client did not receive automatic pickable update. Forced={forcedServerState}, Server={finalServerState}, Client={finalClientState}.");
        Assert.True(
            await PickableSyncTools.WaitForPickableExistsAsync(client, unchangedId),
            "Incremental removed a pickable that was not present in the update payload.");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestPickableSetFlyToPositionSync() {
        PickableState createdState = await PickableSyncTools.CreateAttractablePickableAsync(server);
        Assert.True(createdState.Exists, $"Server did not create an attractable pickable. State={createdState}.");
        PickableState createdClientState = default;
        bool createdSynced = false;
        for (int i = 0; i < 50; i++) {
            await Task.Delay(100);
            createdClientState = await PickableSyncTools.GetPickableStateAsync(client, createdState.Id);
            if (PickableSyncTools.PositionsMatch(createdState, createdClientState)) {
                createdSynced = true;
                break;
            }
        }
        Assert.True(createdSynced, $"Client did not receive pickable {createdState.Id} before attraction. Server={createdState}, Client={createdClientState}.");
        await PickableSyncTools.DelayPickableAutoPickupAsync(client, createdState.Id);

        PickableState serverState = await PickableSyncTools.AttractPickableAsync(server, createdState.Id);
        Assert.True(serverState.Exists && serverState.FlyToPosition.HasValue, $"Server did not attract the pickable. State={serverState}.");

        PickableState clientState = default;
        bool synced = false;
        for (int i = 0; i < 50; i++) {
            await Task.Delay(100);
            clientState = await PickableSyncTools.GetPickableStateAsync(client, serverState.Id);
            if (clientState.Exists
                && clientState.FlyToPosition.HasValue
                && Vector3Dto.DistanceSquared(serverState.FlyToPosition.Value, clientState.FlyToPosition.Value) <= PickableSyncTools.SyncedPositionEpsilonSquared) {
                synced = true;
                break;
            }
        }

        Assert.True(synced, $"Client did not receive PickableSetFlyToPosition sync. Server={serverState}, Client={clientState}.");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
