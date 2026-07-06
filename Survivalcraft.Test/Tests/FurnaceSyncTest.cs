using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class FurnaceSyncTest : IClassFixture<GameFixture> {
    private const float FireTimeEpsilon = 3f;

    private const float SmeltingProgressEpsilon = 0.08f;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public FurnaceSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestFurnaceUpdateSync() {
        await GameTools.CreateTestWorld(server, "furnace-sync");
        int entityId = await FurnaceSyncTools.CreateFurnaceAsync(server);
        Assert.True(entityId > 0, "Server did not create a furnace entity.");

        await FurnaceSyncTools.ConnectTestServerAsync(client, server.GamePort);
        Assert.True(await FurnaceSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive furnace entity {entityId}.");

        await FurnaceSyncTools.StartFurnaceSmeltingAsync(server, entityId);

        await FurnaceSyncTools.WaitForActiveFurnaceStateAsync(client, entityId);

        FurnaceSyncTools.FurnaceState serverState = default;
        FurnaceSyncTools.FurnaceState clientState = default;
        string mismatch = string.Empty;
        bool synced = false;
        for (int i = 0; i < 80; i++) {
            serverState = await FurnaceSyncTools.GetFurnaceStateAsync(server, entityId);
            clientState = await FurnaceSyncTools.GetFurnaceStateAsync(client, entityId);
            if (FurnaceSyncTools.FurnaceStatesMatch(serverState, clientState, out mismatch)) {
                synced = true;
                break;
            }
            await Task.Delay(100);
        }
        Assert.True(
            synced,
            $"Furnace state was not synchronized. {mismatch} Server={serverState}, Client={clientState}.");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
