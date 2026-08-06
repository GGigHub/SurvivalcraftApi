using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class InventorySyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;
    private GameContext spectator => fixture.Spectator;

    public InventorySyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestMoveItemToEmptySlotSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, 0, 0)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, 0, 0)));

        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: 0, toSlot: 1, count: 3);

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 7), (1, InventoryTestTools.DirtContents, 3)));
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestMoveItemMergesSameItemSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, InventoryTestTools.DirtContents, 5)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, InventoryTestTools.DirtContents, 5)));

        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: 0, toSlot: 1, count: 4);

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 6), (1, InventoryTestTools.DirtContents, 9)));
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestMoveItemClampsRequestedCountToSourceSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 2), (1, 0, 0)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 2), (1, 0, 0)));

        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: 0, toSlot: 1, count: 99);

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, 0, 0), (1, InventoryTestTools.DirtContents, 2)));
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestDragDropAllItemsToEmptySlotSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 8), (1, 0, 0)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 8), (1, 0, 0)));

        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 0, toSlot: 1, dragMode: "AllItems");

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, 0, 0), (1, InventoryTestTools.DirtContents, 8)));
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestDragDropSingleItemSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 8), (1, 0, 0)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 8), (1, 0, 0)));

        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 0, toSlot: 1, dragMode: "SingleItem");

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 7), (1, InventoryTestTools.DirtContents, 1)));
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task TestDragDropHalfItemsSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 9), (1, 0, 0)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 9), (1, 0, 0)));

        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 0, toSlot: 1, dragMode: "HalfItems");

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 4), (1, InventoryTestTools.DirtContents, 5)));
    }

    [Fact, TestOrder(Priority = 7)]
    public async Task TestDragDropSwapsDifferentItemsSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 4), (1, InventoryTestTools.CobblestoneContents, 6)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 4), (1, InventoryTestTools.CobblestoneContents, 6)));

        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 0, toSlot: 1, dragMode: "AllItems");

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, InventoryTestTools.CobblestoneContents, 6), (1, InventoryTestTools.DirtContents, 4)));
    }

    [Fact, TestOrder(Priority = 8)]
    public async Task TestActiveSlotSync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 1)), activeSlotIndex: 0);
        await InventoryTestTools.AssertActiveSlotEventuallyAsync(client, playerIndex, 0);

        await InventoryTestTools.SetActiveSlotAsync(client, playerIndex, slotIndex: 4);

        await InventoryTestTools.AssertActiveSlotEventuallyAsync(server, playerIndex, 4);
        await InventoryTestTools.AssertActiveSlotEventuallyAsync(client, playerIndex, 4);
    }

    [Fact, TestOrder(Priority = 9)]
    public async Task TestMoveItemRespectsTargetCapacitySync() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, InventoryTestTools.DirtContents, 39)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, InventoryTestTools.DirtContents, 39)));

        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: 0, toSlot: 1, count: 5);

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 9), (1, InventoryTestTools.DirtContents, 40)));
    }

    [Fact, TestOrder(Priority = 10)]
    public async Task TestDragDropPartialDifferentItemsDoesNotSwap() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        string expected = SlotSpec((0, InventoryTestTools.DirtContents, 4), (1, InventoryTestTools.CobblestoneContents, 6));
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, expected);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, expected);

        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 0, toSlot: 1, dragMode: "SingleItem");

        await AssertInventoryOnServerAndClientAsync(playerIndex, expected);
    }

    [Fact, TestOrder(Priority = 11)]
    public async Task TestMoveItemWithInvalidSlotsDoesNotMutateInventory() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        string expected = SlotSpec((0, InventoryTestTools.DirtContents, 4), (1, InventoryTestTools.CobblestoneContents, 3));
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, expected);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, expected);

        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: -1, toSlot: 1, count: -3);
        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: 0, toSlot: 999, count: 3);

        await AssertInventoryOnServerAndClientAsync(playerIndex, expected);
    }

    [Fact, TestOrder(Priority = 12)]
    public async Task TestDragDropProcessingOnlyWithoutProcessorDoesNotMove() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        string expected = SlotSpec((0, InventoryTestTools.DirtContents, 8), (1, 0, 0));
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, expected);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, expected);

        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 0, toSlot: 1, dragMode: "AllItems", processingOnly: true);

        await AssertInventoryOnServerAndClientAsync(playerIndex, expected);
    }

    [Fact, TestOrder(Priority = 13)]
    public async Task TestDropActiveSlotToWorldSyncsInventoryAndPickable() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 5)), activeSlotIndex: 0);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 5)));

        int pickableId = await InventoryTestTools.DropActiveSlotToWorldAsync(server, playerIndex, count: 2);
        Assert.True(pickableId > 0, "Server did not create a dropped pickable.");

        await AssertInventoryOnServerAndClientAsync(playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 3)));
        await InventoryTestTools.AssertPickableEventuallyAsync(server, pickableId, InventoryTestTools.DirtContents, 2);
        await InventoryTestTools.AssertPickableEventuallyAsync(client, pickableId, InventoryTestTools.DirtContents, 2);
    }

    [Fact, TestOrder(Priority = 14)]
    public async Task TestMoveItemSyncsToSpectatorClient() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await EnsureSpectatorConnectedAsync();
        await InventoryTestTools.WaitForPlayerInventoryAsync(spectator, playerIndex);
        await InventoryTestTools.SeedInventoryAsync(server, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 6), (1, 0, 0)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 6), (1, 0, 0)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(spectator, playerIndex, SlotSpec((0, InventoryTestTools.DirtContents, 6), (1, 0, 0)));

        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: 0, toSlot: 1, count: 2);

        string expected = SlotSpec((0, InventoryTestTools.DirtContents, 4), (1, InventoryTestTools.DirtContents, 2));
        await AssertInventoryOnServerAndClientAsync(playerIndex, expected);
        await InventoryTestTools.AssertInventoryEventuallyAsync(spectator, playerIndex, expected);
    }

    [Fact, TestOrder(Priority = 15)]
    public async Task TestRapidSequentialInventoryActionsConverge() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        await InventoryTestTools.SeedInventoryAsync(
            server,
            playerIndex,
            SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, 0, 0), (2, InventoryTestTools.CobblestoneContents, 3)));
        await InventoryTestTools.AssertInventoryEventuallyAsync(
            client,
            playerIndex,
            SlotSpec((0, InventoryTestTools.DirtContents, 10), (1, 0, 0), (2, InventoryTestTools.CobblestoneContents, 3)));

        await InventoryTestTools.MoveAsync(client, playerIndex, fromSlot: 0, toSlot: 1, count: 4);
        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 0, toSlot: 1, dragMode: "HalfItems");
        await InventoryTestTools.DragDropAsync(client, playerIndex, fromSlot: 2, toSlot: 0, dragMode: "AllItems");
        await InventoryTestTools.SetActiveSlotAsync(client, playerIndex, slotIndex: 2);

        string expected = SlotSpec((0, InventoryTestTools.CobblestoneContents, 3), (1, InventoryTestTools.DirtContents, 7), (2, InventoryTestTools.DirtContents, 3));
        await AssertInventoryOnServerAndClientAsync(playerIndex, expected);
        await InventoryTestTools.AssertActiveSlotEventuallyAsync(server, playerIndex, 2);
        await InventoryTestTools.AssertActiveSlotEventuallyAsync(client, playerIndex, 2);
    }

    [Fact, TestOrder(Priority = 16)]
    public async Task TestClientInteractOpensChestInventoryAtPoint() {
        int playerIndex = await GetReadyPlayerIndexAsync();
        int serverPlayerEntityId = await InventorySyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await InventorySyncTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");

        InventorySyncTools.ChestTarget target = await InventorySyncTools.CreateChestAsync(server);
        Assert.True(target.InventoryId > 0, $"Server did not create a chest inventory. Target={target}.");

        Assert.True(
            await InventorySyncTools.WaitForChestInventoryAsync(client, target),
            $"Client did not receive chest inventory at {target}. Recent logs:{Environment.NewLine}{client.GetRecentLogText()}");

        Assert.True(await InventorySyncTools.InteractWithChestAsync(client, target), $"Client player did not interact with chest at {target}.");

        InventorySyncTools.OpenChestState state = await InventorySyncTools.WaitForOpenChestAsync(client, target);

        Assert.True(state.Opened, $"Client did not open the chest widget. State={state.Raw}");
        Assert.Equal(target.InventoryId, state.InventoryId);
        Assert.Equal(target.Slot0Value, state.Slot0Value);
        Assert.Equal(target.Slot0Count, state.Slot0Count);
    }

    [Fact, TestOrder(Priority = 17)]
    public async Task TestFurnaceUpdateSync() {
        await EnsureWorldConnectedAsync();
        int entityId = await InventorySyncTools.CreateFurnaceAsync(server);
        Assert.True(entityId > 0, "Server did not create a furnace entity.");

        Assert.True(await InventorySyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive furnace entity {entityId}.");

        await InventorySyncTools.StartFurnaceSmeltingAsync(server, entityId);

        await InventorySyncTools.WaitForActiveFurnaceStateAsync(client, entityId);

        InventorySyncTools.FurnaceState serverState = default;
        InventorySyncTools.FurnaceState clientState = default;
        string mismatch = string.Empty;
        bool synced = false;
        for (int i = 0; i < 80; i++) {
            serverState = await InventorySyncTools.GetFurnaceStateAsync(server, entityId);
            clientState = await InventorySyncTools.GetFurnaceStateAsync(client, entityId);
            if (InventorySyncTools.FurnaceStatesMatch(serverState, clientState, out mismatch)) {
                synced = true;
                break;
            }

            await Task.Delay(100);
        }

        Assert.True(
            synced,
            $"Furnace state was not synchronized. {mismatch} Server={serverState}, Client={clientState}.");
    }

    [Fact, TestOrder(Priority = 18)]
    public async Task TestFurnaceInventorySyncAfterServerProcessing() {
        await EnsureWorldConnectedAsync();
        int entityId = await InventorySyncTools.CreateFurnaceAsync(server);
        Assert.True(entityId > 0, "Server did not create a furnace entity.");
        Assert.True(await InventorySyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive furnace entity {entityId}.");

        await InventorySyncTools.StartFurnaceSmeltingAsync(server, entityId);
        InventorySyncTools.FurnaceInventoryState afterStart = await InventorySyncTools.GetFurnaceInventoryAsync(server, entityId);

        InventorySyncTools.FurnaceInventoryState serverInventory = default;
        InventorySyncTools.FurnaceInventoryState clientInventory = default;
        bool fuelConsumed = false;
        for (int i = 0; i < 80; i++) {
            serverInventory = await InventorySyncTools.GetFurnaceInventoryAsync(server, entityId);
            clientInventory = await InventorySyncTools.GetFurnaceInventoryAsync(client, entityId);
            if (serverInventory.Exists
                && clientInventory.Exists
                && serverInventory.FuelCount == 0
                && clientInventory.FuelCount == 0) {
                fuelConsumed = true;
                break;
            }

            await Task.Delay(100);
        }

        Assert.True(
            fuelConsumed,
            $"Furnace fuel was not synchronized after consumption. Server={serverInventory}, Client={clientInventory}.");

        await InventorySyncTools.SetFurnaceProgressAsync(server, entityId, 0.99f);

        bool resultSynchronized = false;
        for (int i = 0; i < 80; i++) {
            serverInventory = await InventorySyncTools.GetFurnaceInventoryAsync(server, entityId);
            clientInventory = await InventorySyncTools.GetFurnaceInventoryAsync(client, entityId);
            if (serverInventory.Exists
                && clientInventory.Exists
                && serverInventory.ResultCount > 0
                && clientInventory.InputCount == serverInventory.InputCount
                && clientInventory.FuelCount == serverInventory.FuelCount
                && clientInventory.ResultCount == serverInventory.ResultCount
                && clientInventory.RemainsCount == serverInventory.RemainsCount) {
                resultSynchronized = true;
                break;
            }

            await Task.Delay(100);
        }

        Assert.True(
            resultSynchronized,
            $"Furnace result inventory was not synchronized. AfterStart={afterStart}, Server={serverInventory}, Client={clientInventory}.");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task ConnectTestServerAsync(GameContext context, int serverPort) {
        await WaitForCommandServerAsync(context);
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await context.WaitLogAsync("Entered screen \"Player\"", 60);
        Assert.True(playerScreen, $"Failed to load the Player screen. Recent logs:{Environment.NewLine}{context.GetRecentLogText()}");

        await context.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await InventoryTestTools.WaitForClientPlayerIndexAsync(context);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
    }

    private async Task AssertInventoryOnServerAndClientAsync(int playerIndex, string expectedSlotSpec) {
        await InventoryTestTools.AssertInventoryEventuallyAsync(server, playerIndex, expectedSlotSpec);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, expectedSlotSpec);
    }

    private async Task<int> GetReadyPlayerIndexAsync() {
        await EnsureWorldConnectedAsync();
        int playerIndex = await InventoryTestTools.WaitForClientPlayerIndexAsync(client);
        await PreparePlayerInventoryAsync(playerIndex);
        return playerIndex;
    }

    private async Task EnsureWorldConnectedAsync() {
        await WaitForCommandServerAsync(server);
        await WaitForCommandServerAsync(client);

        bool serverReady = await TryGetBooleanAsync(
            server,
            "FUNC return Game.GameManager.Project != null;");
        if (!serverReady) {
            await GameTools.CreateTestWorld(server, "inventory-sync");
        }

        bool clientReady = await TryGetBooleanAsync(
            client,
            "FUNC return Game.GameManager.Project != null && Game.NetworkManager.PlayerIndex >= 0;");
        if (!clientReady) {
            await ConnectTestServerAsync(client, server.GamePort);
        }
    }

    private async Task EnsureSpectatorConnectedAsync() {
        if (!spectator.IsStarted) {
            spectator.Start();
            bool mainMenu = await spectator.WaitLogAsync("Entered screen \"MainMenu\"", 60);
            Assert.True(mainMenu, $"Spectator did not enter MainMenu. Recent logs:{Environment.NewLine}{spectator.GetRecentLogText()}");
        }

        await WaitForCommandServerAsync(spectator);
        bool spectatorReady = await TryGetBooleanAsync(
            spectator,
            "FUNC return Game.GameManager.Project != null && Game.NetworkManager.PlayerIndex >= 0;");
        if (!spectatorReady) {
            await ConnectTestServerAsync(spectator, server.GamePort);
        }
    }

    private static async Task WaitForCommandServerAsync(GameContext context) {
        bool ready = await TryGetBooleanAsync(context, "FUNC return true;", attempts: 60, delayMilliseconds: 500);
        Assert.True(ready, $"{context.RoleName} command server was not ready. Recent logs:{Environment.NewLine}{context.GetRecentLogText()}");
    }

    private static async Task<bool> TryGetBooleanAsync(
        GameContext context,
        string command,
        int attempts = 5,
        int delayMilliseconds = 200) {
        for (int i = 0; i < attempts; i++) {
            try {
                object? result = await context.PushCommandAsync(command);
                if (result is bool value && value) {
                    return true;
                }
            }
            catch (SocketException) {
            }
            catch (TimeoutException) {
            }

            await Task.Delay(delayMilliseconds);
        }

        return false;
    }

    private async Task PreparePlayerInventoryAsync(int playerIndex) {
        int entityId = await InventoryTestTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(entityId > 0, $"Server player entity was not available for player {playerIndex}.");
        await InventoryTestTools.WaitForPlayerInventoryAsync(server, playerIndex);
        await InventoryTestTools.WaitForPlayerInventoryAsync(client, playerIndex);
    }

    private static string SlotSpec(params (int SlotIndex, int Contents, int Count)[] slots) =>
        string.Join(
            ';',
            slots.Select(slot =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{slot.SlotIndex}:{slot.Contents}:{slot.Count}")));
}
