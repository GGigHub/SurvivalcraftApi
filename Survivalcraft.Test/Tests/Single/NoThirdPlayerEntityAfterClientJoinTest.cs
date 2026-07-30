using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public sealed class NoThirdPlayerEntityAfterClientJoinTest : IClassFixture<GameFixture>, IDisposable {
    private const string CountPlayerEntitiesCode = """
        var players = GameManager.Project?.FindSubsystem<SubsystemPlayers>();
        if (players == null) {
            return -1;
        }

        return players.ComponentPlayers.Count;
        """;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public NoThirdPlayerEntityAfterClientJoinTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ServerDoesNotCreateThirdPlayerEntityWhenClientJoins() {
        await GameTools.CreateTestWorld(server, "no-third-player-entity");
        await GameTools.ConnectTestServer(client, server.GamePort);

        int playerEntityCount = await TestWaiter.UntilAsync(
            GetServerPlayerEntityCountAsync,
            count => count == 2,
            TestTimeouts.LongEventually,
            $"Server did not contain exactly the host and client player entities. Logs:{Environment.NewLine}{server.GetRecentLogText()}");

        Assert.Equal(2, playerEntityCount);

        await TestWaiter.AssertRemainsAsync(
            GetServerPlayerEntityCountAsync,
            count => count == 2,
            TestTimeouts.ShortObservation,
            $"A third player entity appeared after the client joined. Logs:{Environment.NewLine}{server.GetRecentLogText()}");
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private async Task<int> GetServerPlayerEntityCountAsync() {
        object? result = await server.PushCommandAsync("FUNC " + CountPlayerEntitiesCode);
        return result is int count ? count : Convert.ToInt32(result);
    }
}
