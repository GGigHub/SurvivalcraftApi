using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class PickableSyncTest : IClassFixture<GameFixture> {
    private const float SyncedPositionEpsilonSquared = 0.25f;
    private const int TestBlockContents = 2;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public PickableSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestPickableCreateSync() {
        await GameTools.CreateTestWorld(server);
        await ConnectTestServerForPickableTest(client, server.GamePort);

        int pickableId = await CreatePickableAsync(server, 7, 4f, 2f, 4f);
        Assert.True(pickableId > 0, "Server did not create a pickable.");

        PickableState serverState = await GetPickableStateAsync(server, pickableId);
        PickableState clientState = await WaitForPickableStateAsync(client, pickableId);

        Assert.Equal(serverState.Value, clientState.Value);
        Assert.Equal(serverState.Count, clientState.Count);
        Assert.True(PositionsMatch(serverState, clientState), $"Created pickable position mismatch. Server={serverState}, Client={clientState}.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestPickableDeleteSync() {
        int pickableId = await CreatePickableAsync(server, 3, 5f, 2f, 4f);
        Assert.True(await WaitForPickableExistsAsync(client, pickableId), $"Client did not receive pickable {pickableId} before deletion.");

        int removedId = await RemovePickableAsync(server, pickableId);
        Assert.Equal(pickableId, removedId);

        Assert.True(await WaitForPickableMissingAsync(client, pickableId), $"Client still has removed pickable {pickableId}.");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestPickableUpdateSyncIsAutomaticAndIncremental() {
        int movingId = await CreatePickableAsync(server, 1, 6f, 2f, 4f);
        int unchangedId = await CreatePickableAsync(server, 1, 8f, 2f, 4f);
        PickableState initialMovingClientState = await WaitForPickableStateAsync(client, movingId);
        Assert.True(await WaitForPickableExistsAsync(client, unchangedId), $"Client did not receive unchanged pickable {unchangedId}.");

        await Task.Delay(300);
        PickableState forcedServerState = await ForcePickablePositionAsync(server, movingId, 12f, 4f, 12f);

        PickableState finalServerState = default;
        PickableState finalClientState = default;
        bool synced = false;
        for (int i = 0; i < 50; i++) {
            await Task.Delay(100);
            finalServerState = await GetPickableStateAsync(server, movingId);
            finalClientState = await GetPickableStateAsync(client, movingId);
            if (finalClientState.Exists
                && Vector3Dto.DistanceSquared(initialMovingClientState.Position, finalClientState.Position) > 16f
                && PositionsMatch(finalServerState, finalClientState)) {
                synced = true;
                break;
            }
        }

        Assert.True(
            synced,
            $"Client did not receive automatic pickable update. Forced={forcedServerState}, Server={finalServerState}, Client={finalClientState}.");
        Assert.True(
            await WaitForPickableExistsAsync(client, unchangedId),
            "Incremental removed a pickable that was not present in the update payload.");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestPickableSetFlyToPositionSync() {
        PickableState serverState = await CreateAttractablePickableAsync(server);
        Assert.True(serverState.Exists && serverState.FlyToPosition.HasValue, $"Server did not attract the pickable. State={serverState}.");

        PickableState clientState = default;
        bool synced = false;
        for (int i = 0; i < 50; i++) {
            await Task.Delay(100);
            clientState = await GetPickableStateAsync(client, serverState.Id);
            if (clientState.Exists
                && clientState.FlyToPosition.HasValue
                && Vector3Dto.DistanceSquared(serverState.FlyToPosition.Value, clientState.FlyToPosition.Value) <= SyncedPositionEpsilonSquared) {
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

    private static async Task<int> CreatePickableAsync(GameContext context, int count, float xOffset, float yOffset, float zOffset) {
        object? result = await context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            FUNC var project = Game.GameManager.Project;
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var player = players.ComponentPlayers.FirstOrDefault();
            var basePosition = player?.ComponentBody.Position ?? players.GlobalSpawnPosition;
            if (basePosition == Engine.Vector3.Zero) basePosition = new Engine.Vector3(0f, 80f, 0f);
            var sub = project.FindSubsystem<Game.SubsystemPickables>();
            var position = basePosition + new Engine.Vector3({{xOffset:R}}f, {{yOffset:R}}f, {{zOffset:R}}f);
            var pickable = sub.AddPickable(Game.Terrain.MakeBlockValue({{TestBlockContents}}), {{count}}, position, Engine.Vector3.Zero, null);
            pickable.MaxTimeExist = 9999f;
            return pickable.Id;
            """));
        return (int?)result ?? 0;
    }

    private static async Task<int> RemovePickableAsync(GameContext context, int pickableId) {
        object? result = await context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            FUNC var sub = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>();
            var pickable = sub.Pickables.FirstOrDefault(p => p.Id == {{pickableId}});
            if (pickable == null) return 0;
            pickable.ToRemove = true;
            sub.Update(0f);
            return {{pickableId}};
            """));
        return (int?)result ?? 0;
    }

    private static async Task<PickableState> ForcePickablePositionAsync(GameContext context, int pickableId, float xOffset, float yOffset, float zOffset) {
        string state = (string?)await context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            FUNC var project = Game.GameManager.Project;
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var player = players.ComponentPlayers.FirstOrDefault();
            var basePosition = player?.ComponentBody.Position ?? players.GlobalSpawnPosition;
            if (basePosition == Engine.Vector3.Zero) basePosition = new Engine.Vector3(0f, 80f, 0f);
            var sub = project.FindSubsystem<Game.SubsystemPickables>();
            var pickable = sub.Pickables.FirstOrDefault(p => p.Id == {{pickableId}});
            if (pickable == null) return "missing";
            pickable.Position = basePosition + new Engine.Vector3({{xOffset:R}}f, {{yOffset:R}}f, {{zOffset:R}}f);
            pickable.Velocity = Engine.Vector3.Zero;
            pickable.FlyToPosition = null;
            pickable.MaxTimeExist = 9999f;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "id={0};value={1};count={2};pos={3:R},{4:R},{5:R};fly=null", pickable.Id, pickable.Value, pickable.Count, pickable.Position.X, pickable.Position.Y, pickable.Position.Z);
            """)) ?? string.Empty;
        return ParsePickableState(state);
    }

    private static async Task<PickableState> CreateAttractablePickableAsync(GameContext context) {
        string state = (string?)await context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            FUNC var project = Game.GameManager.Project;
            var sub = project.FindSubsystem<Game.SubsystemPickables>();
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var basePosition = players.GlobalSpawnPosition;
            if (basePosition == Engine.Vector3.Zero) basePosition = new Engine.Vector3(0f, 80f, 0f);
            var position = basePosition + new Engine.Vector3(10f, 2f, 10f);
            var pickable = sub.AddPickable(Game.Terrain.MakeBlockValue({{TestBlockContents}}), 1, position, Engine.Vector3.Zero, null);
            pickable.CreationTime = sub.m_subsystemGameInfo.TotalElapsedGameTime - 1.0;
            pickable.MaxTimeExist = 9999f;
            pickable.FlyToPosition = position + new Engine.Vector3(0.5f, 1f, 0.25f);
            Game.ComponentPickableGatherer.OnPickableAttract?.Invoke(pickable);
            var fly = pickable.FlyToPosition.HasValue ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", pickable.FlyToPosition.Value.X, pickable.FlyToPosition.Value.Y, pickable.FlyToPosition.Value.Z) : "null";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "id={0};value={1};count={2};pos={3:R},{4:R},{5:R};fly={6}", pickable.Id, pickable.Value, pickable.Count, pickable.Position.X, pickable.Position.Y, pickable.Position.Z, fly);
            """)) ?? string.Empty;
        return ParsePickableState(state);
    }

    private static async Task<bool> WaitForPickableExistsAsync(GameContext context, int pickableId) {
        for (int i = 0; i < 50; i++) {
            if ((await GetPickableStateAsync(context, pickableId)).Exists) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task ConnectTestServerForPickableTest(GameContext client, int serverPort) {
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 45);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"", 45);
        Assert.True(playerScreen, "Failed to load the Player screen");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        for (int i = 0; i < 450; i++) {
            bool gameReady = (bool?)await client.PushCommandAsync(
                "FUNC return Game.GameManager.Project != null && ScreensManager.CurrentScreen?.GetType().Name == \"GameScreen\";") ?? false;
            if (gameReady) {
                return;
            }
            await Task.Delay(100);
        }

        Assert.Fail("Client did not enter the game screen after pressing PlayButton.");
    }

    private static async Task<PickableState> WaitForPickableStateAsync(GameContext context, int pickableId) {
        PickableState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetPickableStateAsync(context, pickableId);
            if (state.Exists) {
                return state;
            }
            await Task.Delay(100);
        }
        return state;
    }

    private static async Task<bool> WaitForPickableMissingAsync(GameContext context, int pickableId) {
        for (int i = 0; i < 50; i++) {
            if (!(await GetPickableStateAsync(context, pickableId)).Exists) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task<PickableState> GetPickableStateAsync(GameContext context, int pickableId) {
        string state = (string?)await context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            FUNC var sub = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>();
            var pickable = sub.Pickables.FirstOrDefault(p => p.Id == {{pickableId}});
            if (pickable == null) return "missing";
            var fly = pickable.FlyToPosition.HasValue ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", pickable.FlyToPosition.Value.X, pickable.FlyToPosition.Value.Y, pickable.FlyToPosition.Value.Z) : "null";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "id={0};value={1};count={2};pos={3:R},{4:R},{5:R};fly={6}", pickable.Id, pickable.Value, pickable.Count, pickable.Position.X, pickable.Position.Y, pickable.Position.Z, fly);
            """)) ?? string.Empty;
        return ParsePickableState(state);
    }

    private static bool PositionsMatch(PickableState expected, PickableState actual) =>
        expected.Exists
        && actual.Exists
        && Vector3Dto.DistanceSquared(expected.Position, actual.Position) <= SyncedPositionEpsilonSquared;

    private static PickableState ParsePickableState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new PickableState(false, 0, 0, 0, default, null, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new PickableState(
            true,
            ParseInt(values, "id"),
            ParseInt(values, "value"),
            ParseInt(values, "count"),
            ParseVector3(values["pos"]),
            values.TryGetValue("fly", out string? flyValue) && !flyValue.Equals("null", StringComparison.OrdinalIgnoreCase)
                ? ParseVector3(flyValue)
                : null,
            value);
    }

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    private static Vector3Dto ParseVector3(string value) {
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
            return new Vector3Dto(x, y, z);
        }
        throw new InvalidOperationException($"Invalid Vector3 value: '{value}'.");
    }
}

public readonly record struct PickableState(
    bool Exists,
    int Id,
    int Value,
    int Count,
    Vector3Dto Position,
    Vector3Dto? FlyToPosition,
    string Raw) {
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Exists={Exists}, Id={Id}, Value={Value}, Count={Count}, Position={Position}, FlyToPosition={FlyToPosition}, Raw='{Raw}')");
}
