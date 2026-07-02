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

        int playerIndex = await ConnectTestServerAsync(client, server.GamePort);
        int serverPlayerEntityId = await EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await WaitForMainPlayerAsync(client), "Client main player was not ready.");

        ChestTarget target = await CreateChestAsync(server);
        Assert.True(target.InventoryId > 0, $"Server did not create a chest inventory. Target={target}.");

        Assert.True(
            await WaitForChestInventoryAsync(client, target),
            $"Client did not receive chest inventory at {target}. Recent logs:{Environment.NewLine}{client.GetRecentLogText()}");

        Assert.True(await InteractWithChestAsync(client, target), $"Client player did not interact with chest at {target}.");

        OpenChestState state = await WaitForOpenChestAsync(client, target);

        Assert.True(state.Opened, $"Client did not open the chest widget. State={state.Raw}");
        Assert.Equal(target.InventoryId, state.InventoryId);
        Assert.Equal(target.Slot0Value, state.Slot0Value);
        Assert.Equal(target.Slot0Count, state.Slot0Count);
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task<ChestTarget> CreateChestAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            """
            FUNC var project = Game.GameManager.Project;
            var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
            var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var position = players.GlobalSpawnPosition + new Engine.Vector3(6f, 1f, 6f);
            int x = (int)System.MathF.Floor(position.X);
            int y = (int)System.MathF.Floor(position.Y);
            int z = (int)System.MathF.Floor(position.Z);

            terrain.ChangeCell(x, y, z, Game.Terrain.MakeBlockValue(Game.ChestBlock.Index));

            var blockEntity = blockEntities.GetBlockEntity(x, y, z);
            var chest = blockEntity?.Entity.FindComponent<Game.ComponentChest>();
            if (chest == null) return "missing";

            for (int i = 0; i < chest.SlotsCount; i++) {
                chest.RemoveSlotItems(i, chest.GetSlotCount(i));
            }

            int value = Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index);
            chest.AddSlotItems(0, value, 7);

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "x={0};y={1};z={2};inventoryId={3};slot0Value={4};slot0Count={5}",
                x,
                y,
                z,
                chest.Id,
                chest.GetSlotValue(0),
                chest.GetSlotCount(0));
            """) ?? "missing";

        return ParseChestTarget(raw);
    }

    private static async Task<int> ConnectTestServerAsync(GameContext context, int serverPort) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await WaitForScreenAsync(context, "PlayerScreen");
        Assert.True(playerScreen, $"Failed to load the Player screen. Recent logs:{Environment.NewLine}{context.GetRecentLogText()}");

        await context.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await WaitForClientPlayerIndexAsync(context);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
        return playerIndex;
    }

    private static async Task<int> EnsureServerPlayerEntityAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {{playerIndex}}) {
                    if (playerData.ComponentPlayer == null) {
                        playerData.SpawnPlayer(
                            players.GlobalSpawnPosition + new Engine.Vector3(4f, 0f, 4f),
                            Game.PlayerData.SpawnMode.InitialNoIntro);
                    }
                    return playerData.ComponentPlayer?.Entity.Id ?? 0;
                }
            }
            return 0;
            """);

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> WaitForScreenAsync(GameContext context, string screenTypeName) {
        for (int i = 0; i < 600; i++) {
            object? result = await context.PushCommandAsync(
                "FUNC return Game.ScreensManager.CurrentScreen?.GetType().Name ?? \"\";");
            if (string.Equals(result as string, screenTypeName, StringComparison.Ordinal)) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        for (int i = 0; i < 80; i++) {
            object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
            int playerIndex = result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            if (playerIndex >= 0) {
                return playerIndex;
            }

            await Task.Delay(100);
        }

        return -1;
    }

    private static async Task<bool> WaitForMainPlayerAsync(GameContext context) {
        for (int i = 0; i < 120; i++) {
            object? result = await context.PushCommandAsync(
                """
                FUNC return Game.GameManager.Project
                    .FindSubsystem<Game.SubsystemPlayers>()
                    .ComponentPlayers
                    .Any(p => p.PlayerData.IsMainPlayer && p.ComponentMiner != null);
                """);
            if (result is bool ready && ready) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<bool> WaitForChestInventoryAsync(GameContext context, ChestTarget target) {
        for (int i = 0; i < 80; i++) {
            ChestTarget state = await GetChestInventoryAsync(context, target);
            if (state.Exists
                && state.HasTerrainCell
                && state.InventoryId == target.InventoryId
                && state.Slot0Value == target.Slot0Value
                && state.Slot0Count == target.Slot0Count) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<ChestTarget> GetChestInventoryAsync(GameContext context, ChestTarget target) {
        string raw = (string?)await context.PushCommandAsync(
            $$"""
            FUNC var project = Game.GameManager.Project;
            var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
            var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
            var blockEntity = blockEntities.GetBlockEntity({{target.X}}, {{target.Y}}, {{target.Z}});
            var chest = blockEntity?.Entity.FindComponent<Game.ComponentChest>();
            if (chest == null) return "missing";
            bool hasTerrainCell = Game.Terrain.ExtractContents(terrain.Terrain.GetCellValue({{target.X}}, {{target.Y}}, {{target.Z}})) == Game.ChestBlock.Index;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "x={0};y={1};z={2};inventoryId={3};slot0Value={4};slot0Count={5};hasTerrainCell={6}",
                {{target.X}},
                {{target.Y}},
                {{target.Z}},
                chest.Id,
                chest.GetSlotValue(0),
                chest.GetSlotCount(0),
                hasTerrainCell);
            """) ?? "missing";

        return ParseChestTarget(raw);
    }

    private static async Task<bool> InteractWithChestAsync(GameContext context, ChestTarget target) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var project = Game.GameManager.Project;
            var player = project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
            if (player?.ComponentMiner == null) return false;

            var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
            int value = terrain.Terrain.GetCellValue({{target.X}}, {{target.Y}}, {{target.Z}});
            if (Game.Terrain.ExtractContents(value) != Game.ChestBlock.Index) return false;

            var raycast = new Game.TerrainRaycastResult {
                CellFace = new Game.CellFace({{target.X}}, {{target.Y}}, {{target.Z}}, 0),
                Value = value,
                Ray = new Engine.Ray3(
                    new Engine.Vector3({{target.X}} + 0.5f, {{target.Y}} + 0.5f, {{target.Z}} + 2f),
                    -Engine.Vector3.UnitZ),
                Distance = 1.5f
            };

            return player.ComponentMiner.Interact(raycast);
            """);

        return result is bool interacted && interacted;
    }

    private static async Task<OpenChestState> WaitForOpenChestAsync(GameContext context, ChestTarget target) {
        OpenChestState state = default;
        for (int i = 0; i < 80; i++) {
            state = await GetOpenChestStateAsync(context);
            if (state.Opened
                && state.InventoryId == target.InventoryId
                && state.Slot0Value == target.Slot0Value
                && state.Slot0Count == target.Slot0Count) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    private static async Task<OpenChestState> GetOpenChestStateAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            """
            FUNC var player = Game.GameManager.Project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
            var widget = player?.ComponentGui?.ModalPanelWidget;
            if (widget is not Game.ChestWidget chestWidget) {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "opened=false;widget={0};inventoryId=0;slot0Value=0;slot0Count=0",
                    widget?.GetType().Name ?? "null");
            }

            var chest = chestWidget.m_componentChest;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "opened=true;widget={0};inventoryId={1};slot0Value={2};slot0Count={3}",
                widget.GetType().Name,
                chest.Id,
                chest.GetSlotValue(0),
                chest.GetSlotCount(0));
            """) ?? string.Empty;

        return ParseOpenChestState(raw);
    }

    private static ChestTarget ParseChestTarget(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new ChestTarget(false, 0, 0, 0, 0, 0, 0, false, value);
        }

        Dictionary<string, string> values = ParseKeyValuePairs(value);
        return new ChestTarget(
            true,
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "inventoryId"),
            ParseInt(values, "slot0Value"),
            ParseInt(values, "slot0Count"),
            ParseBool(values, "hasTerrainCell"),
            value);
    }

    private static OpenChestState ParseOpenChestState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        bool opened = values.TryGetValue("opened", out string? openedValue)
            && bool.TryParse(openedValue, out bool result)
            && result;

        return new OpenChestState(
            opened,
            values.TryGetValue("widget", out string? widget) ? widget : string.Empty,
            ParseInt(values, "inventoryId"),
            ParseInt(values, "slot0Value"),
            ParseInt(values, "slot0Count"),
            value);
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    private static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && bool.TryParse(value, out bool result)
        && result;

    private readonly record struct ChestTarget(
        bool Exists,
        int X,
        int Y,
        int Z,
        int InventoryId,
        int Slot0Value,
        int Slot0Count,
        bool HasTerrainCell,
        string Raw);

    private readonly record struct OpenChestState(
        bool Opened,
        string Widget,
        int InventoryId,
        int Slot0Value,
        int Slot0Count,
        string Raw);
}
