using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public sealed class PlayerEntityCountAfterSavedClientDataReconnectTest : IClassFixture<GameFixture>, IDisposable {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;
    private GameContext? restartedServer;

    public PlayerEntityCountAfterSavedClientDataReconnectTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ServerKeepsTwoPlayerEntitiesWhenClientReconnectsToSavedClientData() {
        await GameTools.CreateTestWorld(server, "saved-client-player-data");
        await GameTools.ConnectTestServer(client, server.GamePort);

        string sourceDirectory = server.SourceDirectory;
        string runDirectory = server.RunDirectory;
        await SaveAndKillServerProcessAsync(server);
        await WaitForClientTimeoutDisconnectAsync();
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/DisconnectCode.cs"));
        await WaitForClientMainMenuAsync();

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

        await ConnectSavedClientAsync(client, restartedServer.GamePort);

        await PlayerEntityCountTestTools.AssertCountEventuallyAsync(
            restartedServer,
            2,
            "Restarted server did not contain exactly the saved host and client player entities.");
        await PlayerEntityCountTestTools.AssertCountRemainsAsync(
            restartedServer,
            2,
            "A third player entity appeared when the client reconnected to saved client data.");
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

    private static async Task ConnectSavedClientAsync(GameContext context, int serverPort) {
        await context.PushCommandAsync("EXEC DialogsManager.HideAllDialogs();");
        await context.PushCommandAsync(
            "EXEC " +
            GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs")
                .Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        Assert.True(
            await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", TimeSpan.FromSeconds(45)),
            $"Client did not connect to restarted server. Logs:{Environment.NewLine}{context.GetRecentLogText()}");

        string screen = await TestWaiter.UntilAsync(
            async () => (string?)await context.PushCommandAsync(
                "FUNC return ScreensManager.CurrentScreen?.GetType().Name ?? \"null\";") ?? "null",
            value => value is "PlayerScreen" or "GameScreen",
            TestTimeouts.LongEventually,
            $"Client did not reach PlayerScreen or GameScreen after reconnect. Logs:{Environment.NewLine}{context.GetRecentLogText()}");

        if (screen == "PlayerScreen") {
            await context.PushCommandAsync("CLICK_WIDGET PlayButton");
        }

        await TestWaiter.UntilAsync(
            async () => (string?)await context.PushCommandAsync(
                "FUNC return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"screen={0};state={1}\", ScreensManager.CurrentScreen?.GetType().Name ?? \"null\", NetworkManager.MyState);") ?? string.Empty,
            value => value == "screen=GameScreen;state=Playing",
            TestTimeouts.LongEventually,
            $"Client did not enter Playing after reconnect. Logs:{Environment.NewLine}{context.GetRecentLogText()}");
    }

    private async Task WaitForClientTimeoutDisconnectAsync() {
        Assert.True(
            await client.WaitLogAsync("Reason: Timeout", TimeSpan.FromSeconds(45)),
            $"Client did not time out after the server process terminated. Logs:{Environment.NewLine}{client.GetRecentLogText()}");
        Assert.True(
            await WaitForRecentOrFutureLogAsync(client, "Dialog shown: Disconnected", TestTimeouts.LongEventually),
            $"Client did not show the disconnected dialog. Logs:{Environment.NewLine}{client.GetRecentLogText()}");
    }

    private async Task WaitForClientMainMenuAsync() {
        await WaitForRecentOrFutureLogAsync(
            client,
            "Entered screen \"MainMenu\"",
            TimeSpan.FromSeconds(45),
            "Client did not return to MainMenu after the server shutdown.");

        await TestWaiter.UntilAsync(
            async () => (string?)await client.PushCommandAsync(
                "FUNC return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"screen={0};hasProject={1};isClientRunning={2}\", ScreensManager.CurrentScreen?.GetType().Name ?? \"null\", GameManager.Project != null, NetworkManager.IsClientRunning);") ?? string.Empty,
            state => state == "screen=MainMenuScreen;hasProject=False;isClientRunning=False",
            TestTimeouts.DefaultEventually,
            $"Client did not reach a clean MainMenu state. Logs:{Environment.NewLine}{client.GetRecentLogText()}");
    }

    private static async Task WaitForRecentOrFutureLogAsync(
        GameContext context,
        string message,
        TimeSpan timeout,
        string failureMessage) {
        if (context.GetRecentLogText(1000).Contains(message, StringComparison.Ordinal)) {
            return;
        }

        Assert.True(
            await context.WaitLogAsync(message, timeout),
            $"{failureMessage} Logs:{Environment.NewLine}{context.GetRecentLogText()}");
    }

    private static async Task<bool> WaitForRecentOrFutureLogAsync(
        GameContext context,
        string message,
        TimeSpan timeout) {
        if (context.GetRecentLogText(1000).Contains(message, StringComparison.Ordinal)) {
            return true;
        }

        return await context.WaitLogAsync(message, timeout);
    }
}
