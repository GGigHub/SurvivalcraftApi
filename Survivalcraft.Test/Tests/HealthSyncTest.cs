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
        await HealthSyncTools.ConnectTestServerAsync(client, server.GamePort);
    }

    // 干预：服务端生成一个普通生物并施加一次确定伤害
    // 验证：服务端和客户端看到同一个生物实体的健康值一致降低
    [Fact, TestOrder(Priority = 2)]
    public async Task TestCreatureInjurySync() {
        int entityId = await HealthSyncTools.SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await HealthSyncTools.InjureEntityAsync(server, entityId, 0.25f, "HealthIntegrationTest_Injury");

        Assert.InRange(serverHealth, 0.75d - HealthEpsilon, 0.75d + HealthEpsilon);
        await HealthSyncTools.WaitForHealthAsync(client, entityId, 0.75d);
    }

    // 干预：服务端普通生物受到致死伤害
    // 验证：死亡健康值和死因同步到客户端
    [Fact, TestOrder(Priority = 3)]
    public async Task TestCreatureDeathSync() {
        int entityId = await HealthSyncTools.SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await HealthSyncTools.InjureEntityAsync(server, entityId, 1f, "HealthIntegrationTest_Death");

        Assert.InRange(serverHealth, 0d, HealthEpsilon);
        await HealthSyncTools.WaitForHealthAsync(client, entityId, 0d);
        await HealthSyncTools.WaitForCauseOfDeathAsync(server, entityId, "HealthIntegrationTest_Death");
        await HealthSyncTools.WaitForCauseOfDeathAsync(client, entityId, "HealthIntegrationTest_Death");
    }

    // 干预：服务端恢复生物血量
    // 验证：客户端接收健康同步包后更新为服务端血量
    [Fact, TestOrder(Priority = 4)]
    public async Task TestCreatureHealedHealthSync() {
        int entityId = await HealthSyncTools.SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        // 1. 先把血量扣到 0.5f 以下
        await HealthSyncTools.InjureEntityAsync(server, entityId, 0.6f, "HealthIntegrationTest_Injury");
        await HealthSyncTools.WaitForHealthAtMostAsync(client, entityId, 0.45d);

        // 2. 开启自然恢复并给予极大的恢复系数
        await HealthSyncTools.EnableEntityNaturalHealingAsync(server, entityId);

        // 3. 等待客户端自然收到同步的血量增长
        double clientHealth = await HealthSyncTools.WaitForHealthToExceedAsync(client, entityId, 0.45d);
        Assert.True(clientHealth >= 0.45d, $"Client health {clientHealth} did not increase.");
    }

    // 干预：服务端普通生物受到非致死伤害
    // 验证：血量同步降低，但两端都不会写入死因
    [Fact, TestOrder(Priority = 5)]
    public async Task TestCreatureNonFatalInjuryDoesNotSetCauseOfDeath() {
        int entityId = await HealthSyncTools.SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await HealthSyncTools.InjureEntityAsync(server, entityId, 0.25f, "HealthIntegrationTest_NonFatal");

        Assert.InRange(serverHealth, 0.75d - HealthEpsilon, 0.75d + HealthEpsilon);
        await HealthSyncTools.WaitForHealthAsync(client, entityId, 0.75d);
        await HealthSyncTools.WaitForCauseOfDeathAsync(server, entityId, "");
        await HealthSyncTools.WaitForCauseOfDeathAsync(client, entityId, "");
    }

    // 干预：服务端对低血量生物施加超过剩余血量的伤害
    // 验证：两端血量都被钳制为 0，不会出现负血
    [Fact, TestOrder(Priority = 6)]
    public async Task TestCreatureOverkillDamageClampsHealthToZero() {
        int entityId = await HealthSyncTools.SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");
        await HealthSyncTools.SetEntityHealthAsync(server, entityId, 0.4f);

        double serverHealth = await HealthSyncTools.InjureExistingEntityAsync(server, entityId, 2f, "HealthIntegrationTest_Overkill");

        Assert.InRange(serverHealth, 0d, HealthEpsilon);
        await HealthSyncTools.WaitForHealthAsync(client, entityId, 0d);
        await HealthSyncTools.WaitForCauseOfDeathAsync(server, entityId, "HealthIntegrationTest_Overkill");
        await HealthSyncTools.WaitForCauseOfDeathAsync(client, entityId, "HealthIntegrationTest_Overkill");
    }

    // 干预：生物死亡后再次收到伤害
    // 验证：死亡后的伤害不会重新结算，也不会覆盖第一次死因
    [Fact, TestOrder(Priority = 7)]
    public async Task TestCreatureDeathCauseIsNotOverwrittenByLaterInjury() {
        int entityId = await HealthSyncTools.SpawnHealthTestCreatureAsync(server);
        Assert.True(entityId > 0, "Server did not create a health test creature.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive creature entity {entityId}.");

        double serverHealth = await HealthSyncTools.InjureEntityAsync(server, entityId, 1f, "HealthIntegrationTest_FirstDeath");
        Assert.InRange(serverHealth, 0d, HealthEpsilon);
        await HealthSyncTools.WaitForHealthAsync(client, entityId, 0d);
        await HealthSyncTools.WaitForCauseOfDeathAsync(server, entityId, "HealthIntegrationTest_FirstDeath");
        await HealthSyncTools.WaitForCauseOfDeathAsync(client, entityId, "HealthIntegrationTest_FirstDeath");

        serverHealth = await HealthSyncTools.InjureExistingEntityAsync(server, entityId, 1f, "HealthIntegrationTest_SecondDeath");

        Assert.InRange(serverHealth, 0d, HealthEpsilon);

        await TestWaiter.AssertRemainsAsync(
            () => HealthSyncTools.GetCauseOfDeathAsync(server, entityId),
            cause => cause == "HealthIntegrationTest_FirstDeath",
            TestTimeouts.ShortObservation,
            $"Server cause of death was overwritten for entity {entityId}.");

        await TestWaiter.AssertRemainsAsync(
            () => HealthSyncTools.GetCauseOfDeathAsync(client, entityId),
            cause => cause == "HealthIntegrationTest_FirstDeath",
            TestTimeouts.ShortObservation,
            $"Client cause of death was overwritten for entity {entityId}.");
    }

    // 干预：客户端点击自杀(Choke)按钮
    // 验证：服务端按连接对应玩家结算伤害，并同步回客户端
    [Fact, TestOrder(Priority = 8)]
    public async Task TestClientChokeRequestIsAppliedByServer() {
        int playerIndex = await HealthSyncTools.WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await HealthSyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, serverPlayerEntityId), $"Client did not receive player entity {serverPlayerEntityId}.");
        await HealthSyncTools.SetPlayerHealthAsync(server, playerIndex, 1f);
        await HealthSyncTools.SetPlayerHealthAsync(client, playerIndex, 1f);

        await HealthSyncTools.SimulateClientSuicideButtonClickAsync(client);

        await HealthSyncTools.WaitForPlayerHealthAsync(server, playerIndex, 0.9d);
        await HealthSyncTools.WaitForPlayerHealthAsync(client, playerIndex, 0.9d);
    }

    // 干预：制造物理分歧使得客户端自然承受摔落伤害并发出防作弊保护的请求
    // 验证：坠落伤害由服务端权威结算，服务端拒绝客户端发出的 Fall 请求
    [Fact, TestOrder(Priority = 9)]
    public async Task TestClientFallRequestDoesNotDamageServerPlayer() {
        int playerIndex = await HealthSyncTools.WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await HealthSyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, serverPlayerEntityId), $"Client did not receive player entity {serverPlayerEntityId}.");

        await CreatureSyncTools.WaitForPlayerBodyAsync(server, playerIndex);
        await CreatureSyncTools.WaitForPlayerBodyAsync(client, playerIndex);
        double resetHealth = await HealthSyncTools.SetEntityHealthAsync(server, serverPlayerEntityId, 1f);
        Assert.InRange(resetHealth, 1d - HealthEpsilon, 1d + HealthEpsilon);

        await HealthSyncTools.SimulateForgedClientFallAsync(client, playerIndex);

        await TestWaiter.AssertRemainsAsync(
            () => HealthSyncTools.GetPlayerHealthAsync(server, playerIndex),
            health => Math.Abs(health - 1d) <= HealthEpsilon,
            TestTimeouts.DefaultEventually,
            $"Server accepted forged fall damage for player {playerIndex}.");
    }

    // 干预：服务端玩家的各类生存指标(VitalStats)发生变化
    // 验证：客户端同步接收到对应的变化
    [Fact, TestOrder(Priority = 10)]
    public async Task TestPlayerVitalStatsSync() {
        int playerIndex = await HealthSyncTools.WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await HealthSyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await HealthSyncTools.WaitForEntityExistsAsync(client, serverPlayerEntityId), $"Client did not receive player entity {serverPlayerEntityId}.");

        await HealthSyncTools.SetPlayerVitalStatsAsync(server, playerIndex, 0.3f, 1f, 0.4f, 0f, 12f);

        float[] serverStats = await HealthSyncTools.GetPlayerVitalStatsAsync(server, playerIndex);
        Assert.True(serverStats.Length == 5 && Math.Abs(serverStats[0] - 0.3f) < 0.1f, $"Server stats food was {HealthSyncTools.FormatVitalStats(serverStats)} instead of 0.3");

        await HealthSyncTools.WaitForPlayerVitalStatsAsync(client, playerIndex, 0.3f, 1f, 0.4f, 0f, 12f);
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
