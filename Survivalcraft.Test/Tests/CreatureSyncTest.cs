using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class CreatureSyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    private static string CreatureCountCode => "FUNC " + GameTools.ReadScript("CreatureSync/CreatureCountCode.cs");

    public CreatureSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestEntityListSync() {
        await GameTools.CreateTestWorld(server);
        await GameTools.ConnectTestServer(client, server.GamePort);

        (int serverCount, int clientCount) = await WaitForCreatureCountsEqual();

        Assert.Equal(serverCount, clientCount);
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestCreatureAddSync() {
        string addEntityCode = "FUNC " +
            GameTools.ReadScript("CreatureSync/AddEntityCode.cs");

        int entityId = (int?)await server.PushCommandAsync(addEntityCode) ?? 0;
        Assert.True(entityId > 0, "Server did not create a creature entity.");

        Assert.True(await WaitForEntityExists(client, entityId), $"Client did not receive added creature {entityId}.");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestCreatureRemoveSync() {
        string removeEntityCode = "FUNC " +
            GameTools.ReadScript("CreatureSync/RemoveEntityCode.cs");

        int entityId = (int?)await server.PushCommandAsync(removeEntityCode) ?? 0;
        Assert.True(entityId > 0, "Server did not remove a creature entity.");

        Assert.True(await WaitForEntityMissing(client, entityId), $"Client still has removed creature {entityId}.");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestCreatureBehaviorSync() {
        string forcedCreatures = (string?)await server.PushCommandAsync(ForceCreatureBehaviorCode) ?? string.Empty;
        Dictionary<int, Vector3Dto> initialPositions = ParseCreaturePositions(forcedCreatures);
        Assert.NotEmpty(initialPositions);

        await Task.Delay(500);

        Dictionary<int, Vector3Dto> movedServerPositions = await GetCreaturePositions(server, initialPositions.Keys);

        await server.PushCommandAsync(BuildStopCreatureBehaviorCode(initialPositions.Keys));

        await Task.Delay(200);

        Dictionary<int, Vector3Dto> serverPositions = await GetCreaturePositions(server, initialPositions.Keys);
        Dictionary<int, Vector3Dto> clientPositions = await GetCreaturePositions(client, serverPositions.Keys);

        Assert.NotEmpty(serverPositions);

        foreach ((int entityId, Vector3Dto serverPosition) in serverPositions) {
            Assert.True(
                clientPositions.TryGetValue(entityId, out Vector3Dto clientPosition),
                $"Client is missing synced creature {entityId}.");
            Assert.True(
                Vector3Dto.DistanceSquared(serverPosition, clientPosition) <= SyncedPositionEpsilonSquared,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Creature {entityId} position mismatch. Server={serverPosition}, Client={clientPosition}."));
            Assert.True(
                Vector3Dto.DistanceSquared(initialPositions[entityId], movedServerPositions[entityId]) > MovementEpsilonSquared,
                $"Creature {entityId} did not move after its forced ComponentBehavior was executed.");
        }
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task<int> GetCreatureCount(GameContext context) =>
        (int?)await context.PushCommandAsync(CreatureCountCode) ?? -1;

    private async Task<(int ServerCount, int ClientCount)> WaitForCreatureCountsEqual() {
        int serverCount = await GetCreatureCount(server);
        int clientCount = await GetCreatureCount(client);
        for (int i = 0; i < 100 && serverCount != clientCount; i++) {
            await Task.Delay(100);
            serverCount = await GetCreatureCount(server);
            clientCount = await GetCreatureCount(client);
        }
        return (serverCount, clientCount);
    }

    private static async Task<bool> WaitForEntityExists(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            if (await EntityExists(context, entityId)) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task<bool> WaitForEntityMissing(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            if (!await EntityExists(context, entityId)) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task<bool> EntityExists(GameContext context, int entityId) =>
        (bool?)await context.PushCommandAsync(
            string.Create(CultureInfo.InvariantCulture, $"FUNC Game.GameManager.Project.FindEntity({entityId}) != null")) ?? false;

    private const float MovementEpsilonSquared = 0.0025f;
    private const float SyncedPositionEpsilonSquared = 0.04f;

    private static string ForceCreatureBehaviorCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceCreatureBehaviorCode.cs");

    private static string BuildStopCreatureBehaviorCode(IEnumerable<int> entityIds) {
        string ids = string.Join(",", entityIds);
        return "EXEC " + GameTools.ReadScript("CreatureSync/StopCreatureBehaviorCode.cs").Replace("{0}", ids);
    }

    private static async Task<Dictionary<int, Vector3Dto>> GetCreaturePositions(GameContext context, IEnumerable<int> entityIds) {
        string positions = (string?)await context.PushCommandAsync(BuildGetCreaturePositionsCode(entityIds)) ?? string.Empty;
        Console.WriteLine($"[DEBUG-POSITIONS] Context={context.RoleName}, Positions='{positions}'");
        if (positions == "") throw new InvalidOperationException("Positions string is completely empty!");
        return ParseCreaturePositions(positions);
    }

    private static string BuildGetCreaturePositionsCode(IEnumerable<int> entityIds) {
        string ids = string.Join(",", entityIds);
        return "FUNC " + GameTools.ReadScript("CreatureSync/GetCreaturePositionsCode.cs").Replace("{0}", ids);
    }

    private static Dictionary<int, Vector3Dto> ParseCreaturePositions(string value) {
        Dictionary<int, Vector3Dto> result = [];
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] pair = item.Split(':', 2);
            if (pair.Length != 2
                || !int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entityId)) {
                continue;
            }
            if (pair[1].StartsWith("missing", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Creature {entityId} is missing. Reason: {pair[1]}");
            }

            string[] components = pair[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (components.Length == 3
                && float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                && float.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
                result[entityId] = new Vector3Dto(x, y, z);
            }
        }
        return result;
    }

    private readonly record struct Vector3Dto(float X, float Y, float Z) {
        public static float DistanceSquared(Vector3Dto v1, Vector3Dto v2) {
            float x = v1.X - v2.X;
            float y = v1.Y - v2.Y;
            float z = v1.Z - v2.Z;
            return x * x + y * y + z * z;
        }

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R}, {Z:R})");
    }
}
