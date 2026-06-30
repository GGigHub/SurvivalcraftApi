using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class HealthIntegrationTest : IClassFixture<GameFixture> {
    private const double HealthEpsilon = 0.001d;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public HealthIntegrationTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestWorldCreationAndConnection() {
        await GameTools.CreateTestWorld(server, "health-integration");
        await ConnectTestServerAsync(client, server.GamePort);
    }

    // 干预：服务端生成一个普通生物并施加一次确定伤害
    // 验证：服务端和客户端看到同一个生物实体的健康值一致降低
    [Fact, TestOrder(Priority = 2)]
    public async Task TestCreatureInjurySync() {
        int entityId = await SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await InjureEntityAsync(server, entityId, 0.25f, "HealthIntegrationTest_Injury");

        Assert.InRange(serverHealth, 0.75d - HealthEpsilon, 0.75d + HealthEpsilon);
        await WaitForHealthAsync(client, entityId, 0.75d);
    }

    // 干预：服务端普通生物受到致死伤害
    // 验证：死亡健康值和死因同步到客户端
    [Fact, TestOrder(Priority = 3)]
    public async Task TestCreatureDeathSync() {
        int entityId = await SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await InjureEntityAsync(server, entityId, 1f, "HealthIntegrationTest_Death");

        Assert.InRange(serverHealth, 0d, HealthEpsilon);
        await WaitForHealthAsync(client, entityId, 0d);
        await WaitForCauseOfDeathAsync(server, entityId, "HealthIntegrationTest_Death");
        await WaitForCauseOfDeathAsync(client, entityId, "HealthIntegrationTest_Death");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task ConnectTestServerAsync(GameContext client, int serverPort) {
        await client.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString()));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"", 60);
        Assert.True(playerScreen, "Failed to load the Player screen.");
    }

    private static async Task<int> SpawnHealthTestCreatureAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            """
            FUNC var project = Game.GameManager.Project;
            var spawn = project.FindSubsystem<Game.SubsystemCreatureSpawn>();
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var position = players.GlobalSpawnPosition + new Engine.Vector3(2f, 0f, 2f);
            var entity = spawn.SpawnCreature("Duck", position, false);
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health != null) {
                health.IsInvulnerable = false;
                health.m_regenerateLifeEnabled = false;
                health.Health = 1f;
                health.m_lastHealth = 1f;
                health.CauseOfDeath = null;
                health.DeathTime = null;
            }
            return entity?.Id ?? 0;
            """);

        return result is int id ? id : Convert.ToInt32(result);
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

    private static async Task<double> InjureEntityAsync(GameContext context, int entityId, float amount, string cause) {
        object? result = await context.PushCommandAsync(
            $"""
            FUNC var entity = Game.GameManager.Project.FindEntity({entityId});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health == null) return -1f;
            health.IsInvulnerable = false;
            health.m_regenerateLifeEnabled = false;
            health.Health = 1f;
            health.m_lastHealth = 1f;
            health.CauseOfDeath = null;
            health.DeathTime = null;
            health.Injure({amount:R}f, null, true, "{cause}");
            return health.Health;
            """);

        return Convert.ToDouble(result);
    }

    private static async Task<double> WaitForHealthAsync(GameContext context, int entityId, double expectedHealth) {
        for (int i = 0; i < 50; i++) {
            double health = await GetHealthAsync(context, entityId);
            if (Math.Abs(health - expectedHealth) <= HealthEpsilon) {
                return health;
            }
            await Task.Delay(100);
        }

        double actualHealth = await GetHealthAsync(context, entityId);
        throw new TimeoutException($"Health did not reach {expectedHealth} for entity {entityId}. Actual={actualHealth}.");
    }

    private static async Task<double> GetHealthAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            $"""
            FUNC var entity = Game.GameManager.Project.FindEntity({entityId});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            return health?.Health ?? -1f;
            """);

        return Convert.ToDouble(result);
    }

    private static async Task<string> WaitForCauseOfDeathAsync(GameContext context, int entityId, string expectedCause) {
        for (int i = 0; i < 50; i++) {
            string cause = await GetCauseOfDeathAsync(context, entityId);
            if (cause == expectedCause) {
                return cause;
            }
            await Task.Delay(100);
        }

        string actualCause = await GetCauseOfDeathAsync(context, entityId);
        throw new TimeoutException($"CauseOfDeath did not reach \"{expectedCause}\" for entity {entityId}. Actual=\"{actualCause}\".");
    }

    private static async Task<string> GetCauseOfDeathAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            $"""
            FUNC var entity = Game.GameManager.Project.FindEntity({entityId});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            return health?.CauseOfDeath ?? "";
            """);

        return result as string ?? string.Empty;
    }
}
