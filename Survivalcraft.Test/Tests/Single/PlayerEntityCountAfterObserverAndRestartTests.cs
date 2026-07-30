using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public sealed class PlayerEntityCountAfterObserverJoinTest : IClassFixture<GameFixture>, IDisposable {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public PlayerEntityCountAfterObserverJoinTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ServerKeepsThreePlayerEntitiesAfterClientAndObserverJoin() {
        await GameTools.CreateTestWorld(server, "player-entity-observer-join");
        await GameTools.ConnectTestServer(client, server.GamePort);
        await PlayerSyncTools.ConnectSpectatorAsync(fixture, server.GamePort);

        await PlayerEntityCountTestTools.AssertCountEventuallyAsync(
            server,
            3,
            "Server did not contain exactly the host, client, and observer player entities.");
        await PlayerEntityCountTestTools.AssertCountRemainsAsync(
            server,
            3,
            "A fourth player entity appeared after the observer joined.");
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}

public sealed class PlayerEntityCountAfterServerRestartTest : IClassFixture<GameFixture>, IDisposable {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;
    private GameContext? restartedServer;

    public PlayerEntityCountAfterServerRestartTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task RestartedServerKeepsTwoPlayerEntitiesAfterClientJoins() {
        await GameTools.CreateTestWorld(server, "player-entity-server-restart");

        string sourceDirectory = server.SourceDirectory;
        string runDirectory = server.RunDirectory;
        await SaveAndKillServerProcessAsync(server);

        restartedServer = new GameContext("RestartedServer", sourceDirectory, runDirectory);
        restartedServer.Start();
        Assert.True(
            await restartedServer.WaitLogAsync("Entered screen \"MainMenu\"", TimeSpan.FromSeconds(60)),
            $"Restarted server did not enter MainMenu. Logs:{Environment.NewLine}{restartedServer.GetRecentLogText()}");

        await restartedServer.PushCommandAsync(
            "EXEC " +
            "WorldsManager.UpdateWorldsList();" +
            "if (WorldsManager.WorldInfos.Count == 0) throw new InvalidOperationException(\"No world to restart.\");" +
            "ScreensManager.SwitchScreen(\"GameLoading\", WorldsManager.WorldInfos[0], null);");
        Assert.True(
            await restartedServer.WaitLogAsync("Server started", TestTimeouts.LongEventually),
            $"Restarted server did not start networking. Logs:{Environment.NewLine}{restartedServer.GetRecentLogText()}");
        Assert.True(
            await restartedServer.WaitLogAsync("Entered screen \"Game\"", TestTimeouts.LongEventually),
            $"Restarted server did not enter Game. Logs:{Environment.NewLine}{restartedServer.GetRecentLogText()}");

        await GameTools.ConnectTestServer(client, restartedServer.GamePort);

        await PlayerEntityCountTestTools.AssertCountEventuallyAsync(
            restartedServer,
            2,
            "Restarted server did not contain exactly the host and client player entities.");
        await PlayerEntityCountTestTools.AssertCountRemainsAsync(
            restartedServer,
            2,
            "A third player entity appeared after the client joined the restarted server.");
    }

    public void Dispose() {
        restartedServer?.Dispose();
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task SaveAndKillServerProcessAsync(GameContext context) {
        object? scheduled = await context.PushCommandAsync(
            "FUNC " +
            "GameManager.SaveProject(true, true);" +
            "System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(200); System.Diagnostics.Process.GetCurrentProcess().Kill(); });" +
            "return true;");
        Assert.True(scheduled is true, $"Server shutdown was not scheduled. Result={scheduled}");
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
}

internal static class PlayerEntityCountTestTools {
    private const string CountPlayerEntitiesCode = """
        var players = GameManager.Project?.FindSubsystem<SubsystemPlayers>();
        if (players == null) {
            return -1;
        }

        return players.ComponentPlayers.Count;
        """;

    public static async Task AssertCountEventuallyAsync(
        GameContext context,
        int expectedCount,
        string failureMessage) {
        int count = await TestWaiter.UntilAsync(
            () => GetCountAsync(context),
            value => value == expectedCount,
            TestTimeouts.LongEventually,
            $"{failureMessage} Logs:{Environment.NewLine}{context.GetRecentLogText()}");
        Assert.Equal(expectedCount, count);
    }

    public static Task AssertCountRemainsAsync(
        GameContext context,
        int expectedCount,
        string failureMessage) =>
        TestWaiter.AssertRemainsAsync(
            () => GetCountAsync(context),
            value => value == expectedCount,
            TestTimeouts.ShortObservation,
            $"{failureMessage} Logs:{Environment.NewLine}{context.GetRecentLogText()}");

    private static async Task<int> GetCountAsync(GameContext context) {
        object? result = await context.PushCommandAsync("FUNC " + CountPlayerEntitiesCode);
        return result is int count
            ? count
            : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }
}
