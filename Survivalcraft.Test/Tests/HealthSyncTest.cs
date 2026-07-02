using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class HealthSyncTest : IClassFixture<GameFixture> {
    private const double HealthEpsilon = 0.001d;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public HealthSyncTest(GameFixture fixture) {
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

    // 干预：服务端恢复生物血量
    // 验证：客户端接收健康同步包后更新为服务端血量
    [Fact, TestOrder(Priority = 4)]
    public async Task TestCreatureHealedHealthSync() {
        int entityId = await SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        // 1. 先把血量扣到 0.5f 以下
        await InjureEntityAsync(server, entityId, 0.6f, "HealthIntegrationTest_Injury");
        await WaitForHealthAsync(client, entityId, 0.4d);

        // 2. 开启自然恢复并给予极大的恢复系数
        await EnableEntityNaturalHealingAsync(server, entityId);

        // 3. 等待客户端自然收到同步的血量增长
        double clientHealth = await WaitForHealthToExceedAsync(client, entityId, 0.45d);
        Assert.True(clientHealth >= 0.45d, $"Client health {clientHealth} did not increase.");
    }

    // 干预：服务端普通生物受到非致死伤害
    // 验证：血量同步降低，但两端都不会写入死因
    [Fact, TestOrder(Priority = 5)]
    public async Task TestCreatureNonFatalInjuryDoesNotSetCauseOfDeath() {
        int entityId = await SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await InjureEntityAsync(server, entityId, 0.25f, "HealthIntegrationTest_NonFatal");

        Assert.InRange(serverHealth, 0.75d - HealthEpsilon, 0.75d + HealthEpsilon);
        await WaitForHealthAsync(client, entityId, 0.75d);
        await WaitForCauseOfDeathAsync(server, entityId, "");
        await WaitForCauseOfDeathAsync(client, entityId, "");
    }

    // 干预：服务端对低血量生物施加超过剩余血量的伤害
    // 验证：两端血量都被钳制为 0，不会出现负血
    [Fact, TestOrder(Priority = 6)]
    public async Task TestCreatureOverkillDamageClampsHealthToZero() {
        int entityId = await SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");
        await SetEntityHealthAsync(server, entityId, 0.4f);

        double serverHealth = await InjureExistingEntityAsync(server, entityId, 2f, "HealthIntegrationTest_Overkill");

        Assert.InRange(serverHealth, 0d, HealthEpsilon);
        await WaitForHealthAsync(client, entityId, 0d);
        await WaitForCauseOfDeathAsync(server, entityId, "HealthIntegrationTest_Overkill");
        await WaitForCauseOfDeathAsync(client, entityId, "HealthIntegrationTest_Overkill");
    }

    // 干预：生物死亡后再次收到伤害
    // 验证：死亡后的伤害不会重新结算，也不会覆盖第一次死因
    [Fact, TestOrder(Priority = 7)]
    public async Task TestCreatureDeathCauseIsNotOverwrittenByLaterInjury() {
        int entityId = await SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await InjureEntityAsync(server, entityId, 1f, "HealthIntegrationTest_FirstDeath");
        Assert.InRange(serverHealth, 0d, HealthEpsilon);
        await WaitForHealthAsync(client, entityId, 0d);
        await WaitForCauseOfDeathAsync(server, entityId, "HealthIntegrationTest_FirstDeath");
        await WaitForCauseOfDeathAsync(client, entityId, "HealthIntegrationTest_FirstDeath");

        serverHealth = await InjureExistingEntityAsync(server, entityId, 1f, "HealthIntegrationTest_SecondDeath");

        Assert.InRange(serverHealth, 0d, HealthEpsilon);
        await Task.Delay(500);
        await WaitForCauseOfDeathAsync(server, entityId, "HealthIntegrationTest_FirstDeath");
        await WaitForCauseOfDeathAsync(client, entityId, "HealthIntegrationTest_FirstDeath");
    }

    // 干预：客户端点击自杀(Choke)按钮
    // 验证：服务端按连接对应玩家结算伤害，并同步回客户端
    [Fact, TestOrder(Priority = 8)]
    public async Task TestClientChokeRequestIsAppliedByServer() {
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await WaitForEntityExistsAsync(client, serverPlayerEntityId), $"Client did not receive player entity {serverPlayerEntityId}.");
        await SetPlayerHealthAsync(server, playerIndex, 1f);
        await SetPlayerHealthAsync(client, playerIndex, 1f);

        await SimulateClientSuicideButtonClickAsync(client);

        await WaitForPlayerHealthAsync(server, playerIndex, 0.9d);
        await WaitForPlayerHealthAsync(client, playerIndex, 0.9d);
    }

    // 干预：制造物理分歧使得客户端自然承受摔落伤害并发出防作弊保护的请求
    // 验证：坠落伤害由服务端权威结算，服务端拒绝客户端发出的 Fall 请求
    [Fact, TestOrder(Priority = 9)]
    public async Task TestClientFallRequestDoesNotDamageServerPlayer() {
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await WaitForEntityExistsAsync(client, serverPlayerEntityId), $"Client did not receive player entity {serverPlayerEntityId}.");

        await Task.Delay(2000);
        await SetEntityHealthAsync(server, serverPlayerEntityId, 1f);

        await SimulateForgedClientFallAsync(client, playerIndex);
        await Task.Delay(1500);

        double serverHealth = await GetPlayerHealthAsync(server, playerIndex);
        Assert.InRange(serverHealth, 1d - HealthEpsilon, 1d + HealthEpsilon);
    }

    // 干预：服务端玩家的各类生存指标(VitalStats)发生变化
    // 验证：客户端同步接收到对应的变化
    [Fact, TestOrder(Priority = 10)]
    public async Task TestPlayerVitalStatsSync() {
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await WaitForEntityExistsAsync(client, serverPlayerEntityId), $"Client did not receive player entity {serverPlayerEntityId}.");

        await SetPlayerVitalStatsAsync(server, playerIndex, 0.3f, 1f, 0.4f, 0f, 12f);

        float[] serverStats = await GetPlayerVitalStatsAsync(server, playerIndex);
        Assert.True(serverStats.Length == 5 && Math.Abs(serverStats[0] - 0.3f) < 0.1f, $"Server stats food was {FormatVitalStats(serverStats)} instead of 0.3");

        await WaitForPlayerVitalStatsAsync(client, playerIndex, 0.3f, 1f, 0.4f, 0f, 12f);
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

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
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

    private static async Task<double> InjureExistingEntityAsync(GameContext context, int entityId, float amount, string cause) {
        object? result = await context.PushCommandAsync(
            $"""
            FUNC var entity = Game.GameManager.Project.FindEntity({entityId});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health == null) return -1f;
            health.IsInvulnerable = false;
            health.m_regenerateLifeEnabled = false;
            health.Injure({amount:R}f, null, true, "{cause}");
            return health.Health;
            """);

        return Convert.ToDouble(result);
    }

    private static async Task<double> SetEntityHealthAsync(GameContext context, int entityId, float value) {
        object? result = await context.PushCommandAsync(
            $"""
            FUNC var entity = Game.GameManager.Project.FindEntity({entityId});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health == null) return -1f;
            health.IsInvulnerable = false;
            health.m_regenerateLifeEnabled = false;
            health.Health = {value:R}f;
            health.m_lastHealth = {value:R}f;
            health.CauseOfDeath = null;
            health.DeathTime = null;
            return health.Health;
            """);

        return Convert.ToDouble(result);
    }

    private static async Task<double> WaitForHealthToExceedAsync(GameContext context, int entityId, double expectedMinHealth) {
        for (int i = 0; i < 100; i++) {
            double health = await GetHealthAsync(context, entityId);
            if (health >= expectedMinHealth) {
                return health;
            }
            await Task.Delay(100);
        }

        double actualHealth = await GetHealthAsync(context, entityId);
        throw new TimeoutException($"Health did not exceed {expectedMinHealth} for entity {entityId}. Actual={actualHealth}.");
    }

    private static async Task EnableEntityNaturalHealingAsync(GameContext context, int entityId) {
        await context.PushCommandAsync(
            $$"""
            FUNC var entity = Game.GameManager.Project.FindEntity({{entityId}});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health != null) {
                health.m_regenerateLifeEnabled = true;
                // Use a periodic task to forcefully inject healing over time so it bypasses any creative mode/sleep restrictions
                // and naturally triggers OnHealthHealUpdate which queues the network packet.
                Action loop = null;
                loop = () => {
                    if (health.Health < 1f) {
                        health.Heal(0.02f);
                        Game.ComponentHealth.OnHealthHealUpdate?.Invoke(health);
                        Engine.Time.QueueTimeDelayedExecution(Engine.Time.RealTime + 0.1, loop);
                    }
                };
                loop();
            }
            return true;
            """);
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

    private static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
            int playerIndex = result is int id ? id : Convert.ToInt32(result);
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

        return result is int id ? id : Convert.ToInt32(result);
    }

    private static async Task<double> SetPlayerHealthAsync(GameContext context, int playerIndex, float value) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {{playerIndex}} && playerData.ComponentPlayer != null) {
                    var health = playerData.ComponentPlayer.ComponentHealth;
                    health.IsInvulnerable = false;
                    health.m_regenerateLifeEnabled = false;
                    health.Health = {{value:R}}f;
                    health.m_lastHealth = {{value:R}}f;
                    health.CauseOfDeath = null;
                    health.DeathTime = null;
                    return health.Health;
                }
            }
            return -1f;
            """);

        return Convert.ToDouble(result);
    }

    private static async Task<double> WaitForPlayerHealthAsync(GameContext context, int playerIndex, double expectedHealth) {
        for (int i = 0; i < 50; i++) {
            double health = await GetPlayerHealthAsync(context, playerIndex);
            if (Math.Abs(health - expectedHealth) <= HealthEpsilon) {
                return health;
            }
            await Task.Delay(100);
        }

        double actualHealth = await GetPlayerHealthAsync(context, playerIndex);
        throw new TimeoutException($"Player {playerIndex} health did not reach {expectedHealth}. Actual={actualHealth}.");
    }

    private static async Task<double> GetPlayerHealthAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {{playerIndex}} && playerData.ComponentPlayer != null) {
                    return playerData.ComponentPlayer.ComponentHealth.Health;
                }
            }
            return -1f;
            """);

        return Convert.ToDouble(result);
    }

    private static async Task SimulateClientSuicideButtonClickAsync(GameContext context) {
        await ClickWidgetAndWaitAsync(context, "ClothingButton", IsClothingWidgetVisibleAsync);
        await ClickWidgetAndWaitAsync(context, "VitalStatsButton", IsVitalStatsWidgetVisibleAsync);
        await ClickWidgetAndWaitAsync(context, "ChokeButton", IsVitalStatsWidgetVisibleAsync);
    }

    private static async Task SimulateForgedClientFallAsync(GameContext client, int playerIndex) {
        // Trigger Injure on the client side with a Fall cause.
        // This will naturally broadcast an Injure packet to the server as if the client just fell.
        // The server will evaluate this packet and ignore it because the server proxy is standing.
        await client.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var p in players.PlayersData) {
                if (p.PlayerIndex == {{playerIndex}} && p.ComponentPlayer != null) {
                    p.ComponentPlayer.ComponentHealth.Injure(0.1f, null, false, "Fall");
                }
            }
            return true;
            """);
    }

    private static async Task SetPlayerVitalStatsAsync(GameContext context, int playerIndex, float food, float stamina, float sleep, float wetness, float temperature) {
        await context.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {{playerIndex}} && playerData.ComponentPlayer != null) {
                    var stats = playerData.ComponentPlayer.ComponentVitalStats;
                    if (stats != null) {
                        stats.Food = {{food}}f;
                        stats.Stamina = {{stamina}}f;
                        stats.Sleep = {{sleep}}f;
                        stats.Wetness = {{wetness}}f;
                        stats.Temperature = {{temperature}}f;
                    }
                    return true;
                }
            }
            return false;
            """);
    }

    private static async Task<float[]> GetPlayerVitalStatsAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {{playerIndex}} && playerData.ComponentPlayer != null) {
                    var stats = playerData.ComponentPlayer.ComponentVitalStats;
                    if (stats != null) {
                        return stats.Food.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Stamina.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Sleep.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Wetness.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
            return "";
            """);

        string res = result as string ?? "";
        if (string.IsNullOrEmpty(res)) return Array.Empty<float>();
        string[] parts = res.Split(',');
        return new float[] {
            float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static async Task WaitForPlayerVitalStatsAsync(GameContext context, int playerIndex, float food, float stamina, float sleep, float wetness, float temperature) {
        for (int i = 0; i < 100; i++) {
            float[] stats = await GetPlayerVitalStatsAsync(context, playerIndex);
            if (stats.Length == 5) {
                // Only strictly verify food and sleep because they change very slowly.
                // Stamina, wetness, and temperature simulate too quickly towards their target values to assert exact limits after a network delay.
                if (Math.Abs(stats[0] - food) <= 0.1f &&
                    Math.Abs(stats[2] - sleep) <= 0.1f) {
                    return;
                }
            }
            await Task.Delay(100);
        }

        float[] actualStats = await GetPlayerVitalStatsAsync(context, playerIndex);
        throw new TimeoutException($"Player {playerIndex} vital stats did not reach {food:R}, {stamina:R}, {sleep:R}, {wetness:R}, {temperature:R}. Actual={FormatVitalStats(actualStats)}.");
    }

    private static async Task ClickWidgetAndWaitAsync(GameContext context, string widgetName, Func<GameContext, Task<bool>> waitUntil) {
        for (int i = 0; i < 20; i++) {
            await context.PushCommandAsync($"CLICK_WIDGET {widgetName}");
            for (int j = 0; j < 10; j++) {
                if (await waitUntil(context)) {
                    return;
                }
                await Task.Delay(100);
            }
        }

        throw new TimeoutException($"Widget click did not reach expected state: {widgetName}.");
    }

    private static async Task<bool> IsClothingWidgetVisibleAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            """
            FUNC var playerIndex = Game.NetworkManager.PlayerIndex;
            var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == playerIndex && playerData.ComponentPlayer != null) {
                    return playerData.ComponentPlayer.ComponentGui.ModalPanelWidget is Game.ClothingWidget;
                }
            }
            return false;
            """);

        return result is bool visible && visible;
    }

    private static async Task<bool> IsVitalStatsWidgetVisibleAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            """
            FUNC var playerIndex = Game.NetworkManager.PlayerIndex;
            var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == playerIndex && playerData.ComponentPlayer != null) {
                    return playerData.ComponentPlayer.ComponentGui.ModalPanelWidget is Game.VitalStatsWidget;
                }
            }
            return false;
            """);

        return result is bool visible && visible;
    }

    private static string FormatVitalStats(float[] stats) {
        return stats.Length == 5
            ? $"{stats[0]:R}, {stats[1]:R}, {stats[2]:R}, {stats[3]:R}, {stats[4]:R}"
            : "none";
    }
}
