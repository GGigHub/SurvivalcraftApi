using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class OpenInventorySyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public OpenInventorySyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestClientInteractOpensChestInventoryAtPoint() {
        await GameTools.CreateTestWorld(server, "open-inventory-sync");

        int playerIndex = await OpenInventorySyncTools.ConnectTestServerAsync(client, server.GamePort);
        int serverPlayerEntityId = await OpenInventorySyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await OpenInventorySyncTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");

        OpenInventorySyncTools.ChestTarget target = await OpenInventorySyncTools.CreateChestAsync(server);
        Assert.True(target.InventoryId > 0, $"Server did not create a chest inventory. Target={target}.");

        Assert.True(
            await OpenInventorySyncTools.WaitForChestInventoryAsync(client, target),
            $"Client did not receive chest inventory at {target}. Recent logs:{Environment.NewLine}{client.GetRecentLogText()}");

        Assert.True(await OpenInventorySyncTools.InteractWithChestAsync(client, target), $"Client player did not interact with chest at {target}.");

        OpenInventorySyncTools.OpenChestState state = await OpenInventorySyncTools.WaitForOpenChestAsync(client, target);

        Assert.True(state.Opened, $"Client did not open the chest widget. State={state.Raw}");
        Assert.Equal(target.InventoryId, state.InventoryId);
        Assert.Equal(target.Slot0Value, state.Slot0Value);
        Assert.Equal(target.Slot0Count, state.Slot0Count);
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
