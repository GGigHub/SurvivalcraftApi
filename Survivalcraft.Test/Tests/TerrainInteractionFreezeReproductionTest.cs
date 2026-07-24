using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class TerrainInteractionFreezeReproductionTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public TerrainInteractionFreezeReproductionTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestClientDoesNotFreezeWhenInteractingWithUnreadyTerrain() {
        await GameTools.CreateTestWorld(server, "terrain-freeze-fix");
        await GameTools.ConnectTestServer(client, server.GamePort);
        Assert.True(await TerrainSyncTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");

        string script = GameTools.ReadScript("TerrainSync/SimulateInteractionWhenTerrainNotReadyCode.cs");

        Task<object?> scriptTask = client.PushCommandAsync("FUNC " + script);
        Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));

        Task completedTask = await Task.WhenAny(scriptTask, timeoutTask);
        Assert.True(completedTask == scriptTask, "Client main thread froze when interacting with unready terrain.");

        object? result = await scriptTask;
        Assert.Equal("completed_without_freeze", result);
    }
}
