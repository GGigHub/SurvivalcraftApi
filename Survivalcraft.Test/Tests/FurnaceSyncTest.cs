using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class FurnaceSyncTest : IClassFixture<GameFixture> {
    private const float FireTimeEpsilon = 3f;
    private const float HeatLevelEpsilon = 0.001f;
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
        int entityId = await CreateFurnaceAsync(server);
        Assert.True(entityId > 0, "Server did not create a furnace entity.");

        await ConnectTestServerAsync(client, server.GamePort);
        Assert.True(await WaitForEntityExistsAsync(client, entityId), $"Client did not receive furnace entity {entityId}.");

        await StartFurnaceSmeltingAsync(server, entityId);

        FurnaceState clientState = await WaitForActiveFurnaceStateAsync(client, entityId);
        FurnaceState serverState = await GetFurnaceStateAsync(server, entityId);

        Assert.True(
            FurnaceStatesMatch(serverState, clientState, out string mismatch),
            $"Furnace state was not synchronized. {mismatch} Server={serverState}, Client={clientState}.");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task<int> CreateFurnaceAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            """
            FUNC var project = Game.GameManager.Project;
            var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
            var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var position = players.GlobalSpawnPosition + new Engine.Vector3(5f, 1f, 5f);
            int x = (int)System.MathF.Floor(position.X);
            int y = (int)System.MathF.Floor(position.Y);
            int z = (int)System.MathF.Floor(position.Z);
            terrain.ChangeCell(x, y, z, Game.Terrain.MakeBlockValue(Game.FurnaceBlock.Index));
            var blockEntity = blockEntities.GetBlockEntity(x, y, z);
            return blockEntity?.Entity.Id ?? 0;
            """);

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task ConnectTestServerAsync(GameContext context, int serverPort) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await WaitForScreenAsync(context, "PlayerScreen");
        Assert.True(playerScreen, $"Failed to load the Player screen. Recent logs:{Environment.NewLine}{context.GetRecentLogText()}");

        await context.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await WaitForClientPlayerIndexAsync(context);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
    }

    private static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
            int playerIndex = result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            if (playerIndex >= 0) {
                return playerIndex;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException("Client player index was not assigned.");
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

    private static async Task StartFurnaceSmeltingAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var entity = Game.GameManager.Project.FindEntity({{entityId}});
            var furnace = entity?.FindComponent<Game.ComponentFurnace>();
            if (furnace == null) return "missing";

            for (int i = 0; i < furnace.SlotsCount; i++) {
                furnace.RemoveSlotItems(i, furnace.GetSlotCount(i));
            }

            furnace.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.RawMeatBlock.Index), 4);
            furnace.AddSlotItems(furnace.FuelSlotIndex, Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index), 1);
            int fuelValue = Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index);
            var fuelBlock = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(fuelValue)];
            furnace.m_fireTimeRemaining = fuelBlock.GetFuelFireDuration(fuelValue);
            furnace.m_heatLevel = fuelBlock.GetFuelHeatLevel(fuelValue);
            furnace.UpdateSmeltingRecipe();
            furnace.m_smeltingProgress = 0.35f;
            return "started";
            """);

        if (result as string != "started") {
            throw new InvalidOperationException($"Failed to start furnace smelting for entity {entityId}. Result={result ?? "null"}.");
        }
    }

    private static async Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync(
                $"FUNC return Game.GameManager.Project.FindEntity({entityId}) != null;");
            if (result is bool exists && exists) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<FurnaceState> WaitForActiveFurnaceStateAsync(GameContext context, int entityId) {
        FurnaceState state = default;
        for (int i = 0; i < 80; i++) {
            state = await GetFurnaceStateAsync(context, entityId);
            if (state.Exists
                && state.FireTimeRemaining > 0f
                && state.HeatLevel > 0f
                && state.SmeltingProgress > 0f) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Furnace {entityId} did not become active on {context.RoleName}. LastState={state}.");
    }

    private static async Task<FurnaceState> GetFurnaceStateAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var entity = Game.GameManager.Project.FindEntity({{entityId}});
            var furnace = entity?.FindComponent<Game.ComponentFurnace>();
            if (furnace == null) return "missing";
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:R}|{1:R}|{2:R}",
                furnace.m_fireTimeRemaining,
                furnace.m_smeltingProgress,
                furnace.m_heatLevel);
            """);

        return ParseFurnaceState(result as string ?? string.Empty);
    }

    private static FurnaceState ParseFurnaceState(string value) {
        if (value == "missing" || string.IsNullOrWhiteSpace(value)) {
            return new FurnaceState(false, 0f, 0f, 0f);
        }

        string[] parts = value.Split('|');
        if (parts.Length != 3) {
            throw new FormatException($"Invalid furnace state: \"{value}\".");
        }

        return new FurnaceState(
            true,
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private static bool FurnaceStatesMatch(FurnaceState expected, FurnaceState actual, out string mismatch) {
        if (!actual.Exists) {
            mismatch = "Client furnace is missing.";
            return false;
        }
        if (Math.Abs(expected.HeatLevel - actual.HeatLevel) > HeatLevelEpsilon) {
            mismatch = $"HeatLevel mismatch. Expected={expected.HeatLevel}, Actual={actual.HeatLevel}.";
            return false;
        }
        if (Math.Abs(expected.FireTimeRemaining - actual.FireTimeRemaining) > FireTimeEpsilon) {
            mismatch = $"FireTimeRemaining mismatch. Expected={expected.FireTimeRemaining}, Actual={actual.FireTimeRemaining}.";
            return false;
        }
        if (Math.Abs(expected.SmeltingProgress - actual.SmeltingProgress) > SmeltingProgressEpsilon) {
            mismatch = $"SmeltingProgress mismatch. Expected={expected.SmeltingProgress}, Actual={actual.SmeltingProgress}.";
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    private readonly record struct FurnaceState(
        bool Exists,
        float FireTimeRemaining,
        float SmeltingProgress,
        float HeatLevel);
}
