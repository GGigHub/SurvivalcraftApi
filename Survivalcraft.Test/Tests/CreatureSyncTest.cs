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

    public CreatureSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestEntityListSync() {
        await GameTools.CreateTestWorld(server);
        await GameTools.ConnectTestServer(client, server.GamePort);

        (int serverCount, int clientCount) = await CreatureTools.WaitForCreatureCountsEqual(server, client);

        Assert.Equal(serverCount, clientCount);
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestCreatureAddSync() {
        string addEntityCode = "FUNC " +
            GameTools.ReadScript("CreatureSync/AddEntityCode.cs");

        int entityId = (int?)await server.PushCommandAsync(addEntityCode) ?? 0;
        Assert.True(entityId > 0, "Server did not create a creature entity.");

        Assert.True(await CreatureTools.WaitForEntityExists(client, entityId), $"Client did not receive added creature {entityId}.");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestCreatureRemoveSync() {
        string removeEntityCode = "FUNC " +
            GameTools.ReadScript("CreatureSync/RemoveEntityCode.cs");

        int entityId = (int?)await server.PushCommandAsync(removeEntityCode) ?? 0;
        Assert.True(entityId > 0, "Server did not remove a creature entity.");

        Assert.True(await CreatureTools.WaitForEntityMissing(client, entityId), $"Client still has removed creature {entityId}.");
    }

    // 干预：所有含有m_componentPathfinding的行为组件
    // 验证：被干预的生物坐标
    [Fact, TestOrder(Priority = 4)]
    public async Task TestCreatureBehaviorSync() {
        string forcedCreatures = (string?)await server.PushCommandAsync(CreatureTools.ForceCreatureBehaviorCode) ?? string.Empty;
        Dictionary<int, Vector3Dto> initialPositions = CreatureTools.ParseCreaturePositions(forcedCreatures);
        Assert.NotEmpty(initialPositions);

        await Task.Delay(500);

        Dictionary<int, Vector3Dto> movedServerPositions = await CreatureTools.GetCreaturePositions(server, initialPositions.Keys);

        await server.PushCommandAsync(CreatureTools.BuildStopCreatureBehaviorCode(initialPositions.Keys));

        await Task.Delay(200);

        Dictionary<int, Vector3Dto> serverPositions = await CreatureTools.GetCreaturePositions(server, initialPositions.Keys);
        Dictionary<int, Vector3Dto> clientPositions = await CreatureTools.GetCreaturePositions(client, serverPositions.Keys);

        Assert.NotEmpty(serverPositions);

        foreach ((int entityId, Vector3Dto serverPosition) in serverPositions) {
            Assert.True(
                clientPositions.TryGetValue(entityId, out Vector3Dto clientPosition),
                $"Client is missing synced creature {entityId}.");
            Assert.True(
                Vector3Dto.DistanceSquared(serverPosition, clientPosition) <= CreatureTools.SyncedPositionEpsilonSquared,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Creature {entityId} position mismatch. Server={serverPosition}, Client={clientPosition}."));
            Assert.True(
                Vector3Dto.DistanceSquared(initialPositions[entityId], movedServerPositions[entityId]) > CreatureTools.MovementEpsilonSquared,
                $"Creature {entityId} did not move after its forced ComponentBehavior was executed.");
        }
    }

    // 干预：所有不含有m_componentPathfinding的行为组件
    // 分别有FishOutOfWater/LookAround/StubbornSteed
    // 验证：被控制实体的BendOrder/LookRandomOrder/HeadShakeOrder
    [Fact, TestOrder(Priority = 4)]
    public async Task TestCreatureBehaviorSync2() {
        Dictionary<string, int> entityIds = CreatureTools.ParseBehaviorEntityIds(
            (string?)await server.PushCommandAsync(CreatureTools.AddNonPathfindingBehaviorEntitiesCode) ?? string.Empty);

        Assert.True(entityIds.TryGetValue("FishOutOfWater", out int fishEntityId) && fishEntityId > 0);
        Assert.True(entityIds.TryGetValue("LookAround", out int lookAroundEntityId) && lookAroundEntityId > 0);
        Assert.True(entityIds.TryGetValue("StubbornSteed", out int stubbornSteedEntityId) && stubbornSteedEntityId > 0);

        foreach (int entityId in entityIds.Values) {
            Assert.True(await CreatureTools.WaitForEntityExists(client, entityId), $"Client did not receive creature {entityId}.");
        }

        await server.PushCommandAsync(CreatureTools.BuildForceNonPathfindingBehaviorCode(entityIds));
        NonPathfindingBehaviorOrders serverOrders = await CreatureTools.GetNonPathfindingBehaviorOrders(server, entityIds);
        CreatureTools.AssertExpectedBehaviorOrders(serverOrders, "Server");

        NonPathfindingBehaviorOrders clientOrders = default;
        bool synced = false;
        for (int i = 0; i < 30; i++) {
            await Task.Delay(100);
            clientOrders = await CreatureTools.GetNonPathfindingBehaviorOrders(client, entityIds);
            if (CreatureTools.HasExpectedBehaviorOrders(clientOrders)) {
                synced = true;
                break;
            }
        }

        Assert.True(
            synced,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Client did not receive non-pathfinding behavior orders. Server={serverOrders}, Client={clientOrders}."));
    }

    // 干预：所有播放了音频的组件行为
    // 验证：生物是否播放了音频
    // 不播放音频的组件行为有DigInMud/FishOutOfWater/SwimAround/SwimAway
    // 因为这些行为除了m_componentPathfinding均没有可探测副作用，所以不为其额外编写测试代码
    [Fact, TestOrder(Priority = 5)]
    public async Task TestCreatureBehaviorSync3() {
    }

    // 干预：指定生物的LookAngles/FlyOrder/Position/Rotation/Velocity
    // 验证：干预项是否被同步(BehaviorSyncOnly为true时不该被同步，反之亦然)
    [Fact, TestOrder(Priority = 6)]
    public async Task TestCreatureBodySync() {
    }


    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
