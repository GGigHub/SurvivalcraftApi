using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class DuplicateEntityReproductionTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public DuplicateEntityReproductionTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestBlockEntityDuplicateKeyOnEntityLoadPacket() {
        await GameTools.CreateTestWorld(server, "duplicate-entity-reproduction");
        await GameTools.ConnectTestServer(client, server.GamePort);

        string result = (string?)await client.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EntitySync/SimulateBlockEntityLoadPacketCollisionCode.cs")) ?? string.Empty;

        Assert.Equal("no_exception", result);
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestEntityIdCollisionOnSpawn() {
        string result = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EntitySync/SimulateEntityIdCollisionOnSpawnCode.cs")) ?? string.Empty;

        Assert.True(
            result.Contains("id_reassigned_collision") || result.Contains("System.InvalidOperationException") || result.Contains("unable_to_spawn_null"),
            $"Expected entity spawn ID collision detection, but got result: '{result}'");
    }
}
