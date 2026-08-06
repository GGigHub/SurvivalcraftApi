using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class CreativeDropSyncTest : IClassFixture<GameFixture> {
    private const int CreativeLargeNumber = 536870911;

    private readonly GameFixture fixture;

    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public CreativeDropSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestCreativeWorldAndClientReady() {
        Assert.True(await EnsureCreativeWorldReadyAsync() >= 0, "Creative world was not ready.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestDragDropCreativeSlotToWorldSyncsClient() {
        int playerIndex = await EnsureCreativeWorldReadyAsync();

        await SeedCreativeSlotAsync(playerIndex, slotIndex: 0, InventoryTestTools.DirtContents);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, $"0:{InventoryTestTools.DirtContents}:{CreativeLargeNumber}");

        int pickableId = await DragDropCreativeSlotToWorldAsync(playerIndex, slotIndex: 0, InventoryTestTools.DirtContents);
        Assert.True(pickableId > 0, "Server did not create a dropped pickable after creative drag-drop.");

        await InventoryTestTools.AssertInventoryEventuallyAsync(server, playerIndex, "0:0:0");
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, "0:0:0");
        await InventoryTestTools.AssertPickableEventuallyAsync(server, pickableId, InventoryTestTools.DirtContents, 1);
        await InventoryTestTools.AssertPickableEventuallyAsync(client, pickableId, InventoryTestTools.DirtContents, 1);
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestDropActiveCreativeSlotToWorldSyncsClient() {
        int playerIndex = await EnsureCreativeWorldReadyAsync();

        await SeedCreativeSlotAsync(playerIndex, slotIndex: 2, InventoryTestTools.CobblestoneContents);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, $"2:{InventoryTestTools.CobblestoneContents}:{CreativeLargeNumber}");

        int pickableId = await DropActiveCreativeSlotToWorldAsync(playerIndex, InventoryTestTools.CobblestoneContents);
        Assert.True(pickableId > 0, "Server did not create a dropped pickable after creative Q-drop.");

        await InventoryTestTools.AssertInventoryEventuallyAsync(server, playerIndex, "2:0:0");
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, "2:0:0");
        await InventoryTestTools.AssertPickableEventuallyAsync(server, pickableId, InventoryTestTools.CobblestoneContents, 1);
        await InventoryTestTools.AssertPickableEventuallyAsync(client, pickableId, InventoryTestTools.CobblestoneContents, 1);
    }

    private async Task SeedCreativeSlotAsync(int playerIndex, int slotIndex, int contents) {
        string result = (string?)await server.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var inventory = playerData?.ComponentPlayer?.ComponentMiner?.Inventory;
        if (inventory == null) return "missing";
        for (int i = 0; i < Math.Min(inventory.SlotsCount, 10); i++) {
            inventory.RemoveSlotItems(i, inventory.GetSlotCount(i));
        }
        inventory.AddSlotItems({{slotIndex}}, Game.Terrain.MakeBlockValue({{contents}}), 1);
        inventory.ActiveSlotIndex = {{slotIndex}};
        Game.Network.InventoryPacketHelpers.QueueInventorySync(inventory);
        return "seeded";
        """) ?? string.Empty;

        if (result != "seeded") {
            throw new InvalidOperationException($"Failed to seed creative slot {slotIndex} for player {playerIndex}. Result={result}.");
        }
    }

    private async Task<int> DragDropCreativeSlotToWorldAsync(int playerIndex, int slotIndex, int contents) {
        object? result = await server.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var player = playerData?.ComponentPlayer;
        var inventory = player?.ComponentMiner?.Inventory;
        if (player == null || inventory == null) return 0;
        int expectedValue = Game.Terrain.MakeBlockValue({{contents}});
        var dragData = new Game.InventoryDragData {
            Inventory = inventory,
            SlotIndex = {{slotIndex}},
            DragMode = Game.DragMode.AllItems,
            Count = Game.ComponentCreativeInventory.m_largeNumber
        };
        player.ViewWidget.DragDrop(
            new Engine.Vector3(0f, 0f, 1f),
            dragData,
            Game.ComponentCreativeInventory.m_largeNumber,
            true);
        var pickable = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>()
            .Pickables.LastOrDefault(p => p.Value == expectedValue && p.Count == 1);
        return pickable?.Id ?? 0;
        """);

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<int> DropActiveCreativeSlotToWorldAsync(int playerIndex, int contents) {
        object? result = await server.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var player = playerData?.ComponentPlayer;
        if (player == null) return 0;
        player.DoDrop();
        int expectedValue = Game.Terrain.MakeBlockValue({{contents}});
        var pickable = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>()
            .Pickables.LastOrDefault(p => p.Value == expectedValue && p.Count == 1);
        return pickable?.Id ?? 0;
        """);

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<int> EnsureCreativeWorldReadyAsync() {
        await WaitForCommandServerAsync(server);
        await WaitForCommandServerAsync(client);

        bool serverReady = await TryGetBooleanAsync(
            server,
            "FUNC return Game.GameManager.Project != null;");
        if (!serverReady) {
            await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "creative-drop-sync");
        }

        bool clientReady = await TryGetBooleanAsync(
            client,
            "FUNC return Game.GameManager.Project != null && Game.NetworkManager.PlayerIndex >= 0;");
        int playerIndex = clientReady
            ? await InventoryTestTools.WaitForClientPlayerIndexAsync(client)
            : await PlayerSyncTools.ConnectAndCreatePlayerAsync(client, server.GamePort, "CreativeDropClient");

        Assert.True(
            await InventoryTestTools.EnsureServerPlayerEntityAsync(server, playerIndex) > 0,
            $"Server player entity was not available for player {playerIndex}.");
        await InventoryTestTools.WaitForPlayerInventoryAsync(server, playerIndex);
        await InventoryTestTools.WaitForPlayerInventoryAsync(client, playerIndex);
        return playerIndex;
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

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
