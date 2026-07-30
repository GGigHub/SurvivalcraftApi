using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

/// <summary>
/// Reproduces reconnecting to a newly created server world after the previous
/// server world was closed. The client used to be able to remain in
/// PlayerData's WaitForPlayerEntity state during the second join.
/// </summary>
public sealed class NewWorldAfterServerShutdownTest : IClassFixture<GameFixture>, IDisposable {
    private static readonly TimeSpan ClientEntryTimeout = TimeSpan.FromSeconds(45);

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public NewWorldAfterServerShutdownTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ClientDoesNotWaitForPlayerEntityAfterServerCreatesNewWorld() {
        // Both worlds are created through the server UI, rather than by directly
        // loading a saved WorldInfo, so this follows the user-visible workflow.
        await GameTools.CreateTestWorld(server, "entity-wait-first-world");
        string firstWorldDirectory = await GetCurrentWorldDirectoryAsync(server);
        await ConnectAndAssertClientPlayingAsync("first world");

        await server.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/DisconnectCode.cs"));
        Assert.True(
            await server.WaitLogAsync("Entered screen \"MainMenu\""),
            $"Server did not close the first world. Logs:{Environment.NewLine}{server.GetRecentLogText()}");
        Assert.True(
            await client.WaitLogAsync("Dialog shown: Disconnected"),
            $"Client did not receive the server shutdown. Logs:{Environment.NewLine}{client.GetRecentLogText()}");

        // Return the client to a clean connection state before joining the new world.
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/DisconnectCode.cs"));
        await WaitForClientMainMenuAsync();

        await GameTools.CreateTestWorld(server, "entity-wait-second-world");
        string secondWorldDirectory = await GetCurrentWorldDirectoryAsync(server);
        Assert.NotEqual(firstWorldDirectory, secondWorldDirectory);

        ClientEntryState secondJoin = await ConnectAndAssertClientPlayingAsync("new world after server shutdown");
        Assert.NotEqual("WaitForPlayerEntity", secondJoin.PlayerState);
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private async Task<ClientEntryState> ConnectAndAssertClientPlayingAsync(string stepName) {
        await client.PushCommandAsync(
            "EXEC " +
            GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs")
                .Replace("{0}", server.GamePort.ToString(CultureInfo.InvariantCulture)));

        Assert.True(
            await client.WaitLogAsync($"Connected: 127.0.0.1:{server.GamePort}", ClientEntryTimeout),
            $"{stepName}: client did not connect. Logs:{Environment.NewLine}{client.GetRecentLogText()}");
        Assert.True(
            await client.WaitLogAsync("Entered screen \"Player\"", ClientEntryTimeout),
            $"{stepName}: client did not enter PlayerScreen. Logs:{Environment.NewLine}{client.GetRecentLogText()}");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");

        return await TestWaiter.UntilAsync(
            GetClientEntryStateAsync,
            state => state.IsPlaying,
            ClientEntryTimeout,
            $"{stepName}: client did not leave WaitForPlayerEntity. Logs:{Environment.NewLine}{client.GetRecentLogText()}");
    }

    private async Task<string> GetCurrentWorldDirectoryAsync(GameContext context) {
        return (string?)await context.PushCommandAsync(
            "FUNC return GameManager.WorldInfo?.DirectoryName ?? string.Empty;")
            ?? string.Empty;
    }

    private async Task<ClientEntryState> GetClientEntryStateAsync() {
        string raw = (string?)await client.PushCommandAsync(
            "FUNC " +
            "var screen = ScreensManager.CurrentScreen?.GetType().Name ?? \"null\";" +
            "var project = GameManager.Project;" +
            "var playerIndex = NetworkManager.PlayerIndex;" +
            "var player = project?.FindSubsystem<SubsystemPlayers>()?.PlayersData.FirstOrDefault(p => p.PlayerIndex == playerIndex);" +
            "var playerState = player?.m_stateMachine.CurrentState ?? \"missing\";" +
            "var hasPlayerEntity = player?.ComponentPlayer != null;" +
            "return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"screen={0};network={1};playerIndex={2};playerState={3};hasPlayerEntity={4}\", screen, NetworkManager.MyState, playerIndex, playerState, hasPlayerEntity);")
            ?? string.Empty;

        return ClientEntryState.Parse(raw);
    }

    private async Task WaitForClientMainMenuAsync() {
        await TestWaiter.UntilAsync(
            async () => (string?)await client.PushCommandAsync(
                "FUNC return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"screen={0};hasProject={1};isClientRunning={2}\", ScreensManager.CurrentScreen?.GetType().Name ?? \"null\", GameManager.Project != null, NetworkManager.IsClientRunning);")
                ?? string.Empty,
            state => state == "screen=MainMenuScreen;hasProject=False;isClientRunning=False",
            TestTimeouts.DefaultEventually,
            $"Client did not return to a clean MainMenu state after the server shutdown. Logs:{Environment.NewLine}{client.GetRecentLogText()}");
    }

    private readonly record struct ClientEntryState(
        string Screen,
        string NetworkState,
        int PlayerIndex,
        string PlayerState,
        bool HasPlayerEntity,
        string Raw) {
        public bool IsPlaying =>
            Screen == "GameScreen"
            && NetworkState == "Playing"
            && PlayerIndex >= 0
            && PlayerState == "Playing"
            && HasPlayerEntity;

        public static ClientEntryState Parse(string raw) {
            string screen = string.Empty;
            string networkState = string.Empty;
            int playerIndex = -1;
            string playerState = string.Empty;
            bool hasPlayerEntity = false;

            foreach (string part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                string[] pair = part.Split('=', 2);
                if (pair.Length != 2) {
                    continue;
                }

                switch (pair[0]) {
                    case "screen":
                        screen = pair[1];
                        break;
                    case "network":
                        networkState = pair[1];
                        break;
                    case "playerIndex":
                        int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out playerIndex);
                        break;
                    case "playerState":
                        playerState = pair[1];
                        break;
                    case "hasPlayerEntity":
                        bool.TryParse(pair[1], out hasPlayerEntity);
                        break;
                }
            }

            return new ClientEntryState(screen, networkState, playerIndex, playerState, hasPlayerEntity, raw);
        }
    }
}
