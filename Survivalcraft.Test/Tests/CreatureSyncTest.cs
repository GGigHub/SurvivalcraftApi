using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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

        string countCode = 
            "FUNC Game.GameManager.Project.FindSubsystem<SubsystemCreatureSpawn>()" +
            "   .Creatures.Count()";

        int serverCount = (int?)await server.PushCommandAsync(countCode) ?? -1;
        int clientCount = (int?)await client.PushCommandAsync(countCode) ?? -1;

        Assert.True(serverCount != -1 && clientCount != -1 && serverCount == clientCount);
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
