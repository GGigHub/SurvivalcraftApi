using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class DamageSyncTest : IClassFixture<GameFixture> {
    private const float HitpointsEpsilon = 0.001f;
    private const float DensityEpsilon = 0.001f;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public DamageSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestWorldCreationAndConnection() {
        await GameTools.CreateTestWorld(server, "damage-sync");
        await ConnectTestServerAsync(client, server.GamePort);
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestComponentDamageHitpointsSyncsToClient() {
        int boatId = await CreateBoatAsync(server, 8f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageEntityState serverState = await DamageBoatAsync(server, boatId, 0.25f);
        Assert.True(serverState.Exists, $"Server did not produce a damage state. State={serverState}.");
        Assert.InRange(serverState.Hitpoints, 0.75f - HitpointsEpsilon, 0.75f + HitpointsEpsilon);

        DamageEntityState clientState = await WaitForDamageEntityStateAsync(client, boatId, serverState.Hitpoints);

        Assert.True(DamageStatesMatch(serverState, clientState), $"ComponentDamage state mismatch. Server={serverState}, Client={clientState}.");
    }

    // 额外测试Density
    [Fact, TestOrder(Priority = 3)]
    public async Task TestBoatDamageThresholdSyncsClientDensity() {
        int boatId = await CreateBoatAsync(server, 10f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageEntityState serverState = await DamageBoatAsync(server, boatId, 0.5f);
        Assert.True(serverState.Exists, $"Server did not produce a damage state. State={serverState}.");
        Assert.InRange(serverState.Hitpoints, 0.5f - HitpointsEpsilon, 0.5f + HitpointsEpsilon);

        DamageEntityState clientState = await WaitForBoatDensityAsync(client, boatId, 0.7f);

        Assert.InRange(clientState.Hitpoints, 0.5f - HitpointsEpsilon, 0.5f + HitpointsEpsilon);
        Assert.InRange(clientState.Density, 0.7f - DensityEpsilon, 0.7f + DensityEpsilon);
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestDestroyedDamageEntityIsRemovedOnClient() {
        int boatId = await CreateBoatAsync(server, 12f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageEntityState serverState = await DamageBoatAsync(server, boatId, 1f);
        Assert.True(serverState.Exists && serverState.Hitpoints <= HitpointsEpsilon, $"Server boat was not destroyed. State={serverState}.");

        Assert.True(await WaitForEntityMissingAsync(client, boatId), $"Client still has destroyed boat entity {boatId}.");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestClientLocalComponentDamageDoesNotChangeServerHitpoints() {
        int boatId = await CreateBoatAsync(server, 14f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageEntityState initialServerState = await GetDamageEntityStateAsync(server, boatId);
        DamageEntityState clientAttemptState = await DamageBoatAsync(client, boatId, 0.25f);
        await Task.Delay(500);
        DamageEntityState finalServerState = await GetDamageEntityStateAsync(server, boatId);

        Assert.True(clientAttemptState.Exists, $"Client boat was missing during local damage attempt. State={clientAttemptState}.");
        Assert.InRange(finalServerState.Hitpoints, initialServerState.Hitpoints - HitpointsEpsilon, initialServerState.Hitpoints + HitpointsEpsilon);
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task TestActiveToolDamageSyncsToClientInventory() {
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await WaitForPlayerInventoryAsync(client, playerIndex), $"Client player inventory was not available for player {playerIndex}.");

        ToolSlotState serverState = await GiveAndDamageActiveToolAsync(server, playerIndex, 5);
        Assert.True(serverState.Exists, $"Server did not produce a damaged tool state. State={serverState}.");
        Assert.Equal(1, serverState.Count);
        Assert.Equal(5, serverState.Damage);

        ToolSlotState clientState = await WaitForToolSlotStateAsync(client, playerIndex, serverState);

        Assert.Equal(serverState.Value, clientState.Value);
        Assert.Equal(serverState.Count, clientState.Count);
        Assert.Equal(serverState.Contents, clientState.Contents);
        Assert.Equal(serverState.Damage, clientState.Damage);
    }

    [Fact, TestOrder(Priority = 7)]
    public async Task TestActiveToolBreakSyncsEmptySlotToClientInventory() {
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await WaitForPlayerInventoryAsync(client, playerIndex), $"Client player inventory was not available for player {playerIndex}.");

        ToolSlotState serverState = await GiveAndBreakActiveToolAsync(server, playerIndex);
        Assert.True(serverState.Exists, $"Server did not produce a broken tool state. State={serverState}.");
        Assert.Equal(0, serverState.Value);
        Assert.Equal(0, serverState.Count);

        ToolSlotState clientState = await WaitForToolSlotStateAsync(client, playerIndex, serverState);

        Assert.Equal(0, clientState.Value);
        Assert.Equal(0, clientState.Count);
        Assert.Equal(serverState.Damage, clientState.Damage);
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task ConnectTestServerAsync(GameContext context, int serverPort) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await context.WaitLogAsync("Entered screen \"Player\"", 60);
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

    private static async Task<bool> WaitForPlayerInventoryAsync(GameContext context, int playerIndex) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync(
                $$"""
                FUNC var player = Game.GameManager.Project
                    .FindSubsystem<Game.SubsystemPlayers>()
                    .ComponentPlayers
                    .FirstOrDefault(p => p.PlayerData.PlayerIndex == {{playerIndex}});
                return player?.ComponentMiner?.Inventory != null;
                """);
            if (result is bool exists && exists) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<ToolSlotState> GiveAndDamageActiveToolAsync(GameContext context, int playerIndex, int damageCount) {
        string raw = (string?)await context.PushCommandAsync(
            $$"""
            FUNC var gameInfo = Game.GameManager.Project.FindSubsystem<Game.SubsystemGameInfo>();
            gameInfo.WorldSettings.GameMode = Game.GameMode.Survival;
            var player = Game.GameManager.Project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.PlayerIndex == {{playerIndex}});
            if (player == null || player.ComponentMiner?.Inventory == null) return "missing";
            var inventory = player.ComponentMiner.Inventory;
            inventory.ActiveSlotIndex = 0;
            inventory.RemoveSlotItems(0, inventory.GetSlotCount(0));
            inventory.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.IronPickaxeBlock.Index), 1);
            player.ComponentMiner.DamageActiveTool({{damageCount}});
            int value = inventory.GetSlotValue(0);
            int count = inventory.GetSlotCount(0);
            int contents = value != 0 ? Game.Terrain.ExtractContents(value) : 0;
            int damage = value != 0 ? Game.BlocksManager.Blocks[contents].GetDamage(value) : -1;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "value={0};count={1};contents={2};damage={3}", value, count, contents, damage);
            """) ?? "missing";

        return ParseToolSlotState(raw);
    }

    private static async Task<ToolSlotState> GiveAndBreakActiveToolAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            $$"""
            FUNC var gameInfo = Game.GameManager.Project.FindSubsystem<Game.SubsystemGameInfo>();
            gameInfo.WorldSettings.GameMode = Game.GameMode.Survival;
            var player = Game.GameManager.Project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.PlayerIndex == {{playerIndex}});
            if (player == null || player.ComponentMiner?.Inventory == null) return "missing";
            var inventory = player.ComponentMiner.Inventory;
            inventory.ActiveSlotIndex = 0;
            inventory.RemoveSlotItems(0, inventory.GetSlotCount(0));
            int value = Game.Terrain.MakeBlockValue(Game.IronPickaxeBlock.Index);
            int durability = Game.BlocksManager.Blocks[Game.IronPickaxeBlock.Index].GetDurability(value);
            inventory.AddSlotItems(0, value, 1);
            player.ComponentMiner.DamageActiveTool(durability + 1);
            value = inventory.GetSlotValue(0);
            int count = inventory.GetSlotCount(0);
            int contents = value != 0 ? Game.Terrain.ExtractContents(value) : 0;
            int damage = value != 0 ? Game.BlocksManager.Blocks[contents].GetDamage(value) : -1;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "value={0};count={1};contents={2};damage={3}", value, count, contents, damage);
            """) ?? "missing";

        return ParseToolSlotState(raw);
    }

    private static async Task<ToolSlotState> WaitForToolSlotStateAsync(GameContext context, int playerIndex, ToolSlotState expected) {
        ToolSlotState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetToolSlotStateAsync(context, playerIndex);
            if (state.Exists
                && state.Value == expected.Value
                && state.Count == expected.Count
                && state.Damage == expected.Damage) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Tool slot did not synchronize for player {playerIndex}. Expected={expected}, Actual={state}.");
    }

    private static async Task<ToolSlotState> GetToolSlotStateAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            $$"""
            FUNC var player = Game.GameManager.Project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.PlayerIndex == {{playerIndex}});
            var inventory = player?.ComponentMiner?.Inventory;
            if (inventory == null) return "missing";
            int value = inventory.GetSlotValue(0);
            int count = inventory.GetSlotCount(0);
            int contents = value != 0 ? Game.Terrain.ExtractContents(value) : 0;
            int damage = value != 0 ? Game.BlocksManager.Blocks[contents].GetDamage(value) : -1;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "value={0};count={1};contents={2};damage={3}", value, count, contents, damage);
            """) ?? "missing";

        return ParseToolSlotState(raw);
    }

    private static ToolSlotState ParseToolSlotState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new ToolSlotState(false, 0, 0, 0, -1, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new ToolSlotState(
            true,
            ParseInt(values, "value"),
            ParseInt(values, "count"),
            ParseInt(values, "contents"),
            ParseInt(values, "damage"),
            value);
    }

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    private readonly record struct ToolSlotState(
        bool Exists,
        int Value,
        int Count,
        int Contents,
        int Damage,
        string Raw);

    private static async Task<int> CreateBoatAsync(GameContext context, float xOffset, float yOffset, float zOffset) {
        object? result = await context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            FUNC var project = Game.GameManager.Project;
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var basePosition = players.GlobalSpawnPosition;
            if (basePosition == Engine.Vector3.Zero) basePosition = new Engine.Vector3(0f, 80f, 0f);
            var entity = Game.DatabaseManager.CreateEntity(project, "Boat", true);
            var body = entity.FindComponent<Game.ComponentBody>(true);
            body.Position = basePosition + new Engine.Vector3({{xOffset:R}}f, {{yOffset:R}}f, {{zOffset:R}}f);
            body.Rotation = Engine.Quaternion.Identity;
            var damage = entity.FindComponent<Game.ComponentDamage>(true);
            damage.Hitpoints = 1f;
            damage.m_lastHitpoints = 1f;
            var spawn = entity.FindComponent<Game.ComponentSpawn>();
            if (spawn != null) spawn.SpawnDuration = 0f;
            project.AddEntity(entity);
            return entity.Id;
            """));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<DamageEntityState> DamageBoatAsync(GameContext context, int entityId, float amount) {
        string raw = (string?)await context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            FUNC var entity = Game.GameManager.Project.FindEntity({{entityId}});
            var damage = entity?.FindComponent<Game.ComponentDamage>();
            var body = entity?.FindComponent<Game.ComponentBody>();
            if (damage == null || body == null) return "missing";
            damage.Damage({{amount:R}}f);
            var boat = entity.FindComponent<Game.ComponentBoat>();
            boat?.Update(0.1f);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "hitpoints={0:R};density={1:R}", damage.Hitpoints, body.Density);
            """)) ?? "missing";

        return ParseDamageEntityState(raw);
    }

    private static async Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync($"FUNC return Game.GameManager.Project.FindEntity({entityId}) != null;");
            if (result is bool exists && exists) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<bool> WaitForEntityMissingAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync($"FUNC return Game.GameManager.Project.FindEntity({entityId}) == null;");
            if (result is bool missing && missing) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<DamageEntityState> WaitForDamageEntityStateAsync(GameContext context, int entityId, float expectedHitpoints) {
        DamageEntityState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetDamageEntityStateAsync(context, entityId);
            if (state.Exists && Math.Abs(state.Hitpoints - expectedHitpoints) <= HitpointsEpsilon) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"ComponentDamage hitpoints did not synchronize for entity {entityId}. Expected={expectedHitpoints}, Actual={state}.");
    }

    private static async Task<DamageEntityState> WaitForBoatDensityAsync(GameContext context, int entityId, float expectedDensity) {
        DamageEntityState state = default;
        for (int i = 0; i < 50; i++) {
            state = await UpdateAndGetDamageEntityStateAsync(context, entityId);
            if (state.Exists && Math.Abs(state.Density - expectedDensity) <= DensityEpsilon) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Boat density did not reach {expectedDensity} for entity {entityId}. Actual={state}.");
    }

    private static async Task<DamageEntityState> GetDamageEntityStateAsync(GameContext context, int entityId) {
        string raw = (string?)await context.PushCommandAsync(
            $$"""
            FUNC var entity = Game.GameManager.Project.FindEntity({{entityId}});
            var damage = entity?.FindComponent<Game.ComponentDamage>();
            var body = entity?.FindComponent<Game.ComponentBody>();
            if (damage == null || body == null) return "missing";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "hitpoints={0:R};density={1:R}", damage.Hitpoints, body.Density);
            """) ?? "missing";

        return ParseDamageEntityState(raw);
    }

    private static async Task<DamageEntityState> UpdateAndGetDamageEntityStateAsync(GameContext context, int entityId) {
        string raw = (string?)await context.PushCommandAsync(
            $$"""
            FUNC var entity = Game.GameManager.Project.FindEntity({{entityId}});
            var damage = entity?.FindComponent<Game.ComponentDamage>();
            var body = entity?.FindComponent<Game.ComponentBody>();
            if (damage == null || body == null) return "missing";
            entity.FindComponent<Game.ComponentBoat>()?.Update(0.1f);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "hitpoints={0:R};density={1:R}", damage.Hitpoints, body.Density);
            """) ?? "missing";

        return ParseDamageEntityState(raw);
    }

    private static bool DamageStatesMatch(DamageEntityState expected, DamageEntityState actual) =>
        expected.Exists
        && actual.Exists
        && Math.Abs(expected.Hitpoints - actual.Hitpoints) <= HitpointsEpsilon;

    private static DamageEntityState ParseDamageEntityState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new DamageEntityState(false, 0f, 0f, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new DamageEntityState(
            true,
            ParseFloat(values, "hitpoints"),
            ParseFloat(values, "density"),
            value);
    }

    private static float ParseFloat(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;

    private readonly record struct DamageEntityState(
        bool Exists,
        float Hitpoints,
        float Density,
        string Raw);
}
