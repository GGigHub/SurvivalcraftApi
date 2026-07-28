using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public sealed class RepeatedWorldReentryTest : IClassFixture<GameFixture>, IDisposable {
    private static readonly TimeSpan NetworkDisconnectTimeout = TimeSpan.FromSeconds(25);
    private static readonly string MovementSequenceName =
        Environment.GetEnvironmentVariable("SC_REENTRY_MOVEMENT_SEQUENCE") ?? "forward_hold";
    private static readonly int HoldMovementFrameCount =
        GetEnvironmentInt32("SC_REENTRY_HOLD_FRAMES", 10000);
    private static readonly int HoldMovementDelayAfterMs =
        GetEnvironmentInt32("SC_REENTRY_HOLD_DELAY_MS", 5000);
    private static readonly HashSet<string>? EnabledMovementSteps =
        CreateEnabledMovementSteps(
            Environment.GetEnvironmentVariable("SC_REENTRY_MOVEMENT_STEPS"));
    private static readonly MovementStep[] MovementSequence = CreateMovementSequence(MovementSequenceName);

    private readonly GameFixture fixture;
    private readonly List<GameContext> ownedServers = [];
    private readonly List<GameContext> ownedClients = [];

    public RepeatedWorldReentryTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ClientCanRepeatedlyReenterSameWorldAcrossServerTimeoutsAndRestarts() {
        GameContext server = fixture.Server;
        GameContext client = fixture.Client;

        await GameTools.CreateTestWorld(server, "repeated-world-reentry");
        string serverRunDirectory = server.RunDirectory;
        string serverSourceDirectory = server.SourceDirectory;
        await AssertSingleWorldExistsAsync(server);

        await ConnectClientAndEnterPlayingAsync(client, server.GamePort, "first client join");

        await SaveAndKillServerProcessAsync(server);
        await WaitForClientTimeoutDisconnectAsync(client, "first server shutdown");

        server = await RestartServerFromOriginalWorldAsync(serverSourceDirectory, serverRunDirectory, "RestartA");
        await ConnectClientAndEnterPlayingAsync(client, server.GamePort, "client join after first server restart");

        client = await KillAndRestartClientProcessAsync(client, "RestartClientA");
        await WaitForServerSessionCountAsync(
            server,
            0,
            NetworkDisconnectTimeout + TestTimeouts.LongEventually,
            "server did not remove the client after the client process was killed");
        await ConnectClientAndEnterPlayingAsync(client, server.GamePort, "client rejoin while server stayed open");

        await SaveAndKillServerProcessAsync(server);
        await WaitForClientTimeoutDisconnectAsync(client, "second server shutdown");

        server = await RestartServerFromOriginalWorldAsync(serverSourceDirectory, serverRunDirectory, "RestartB");
        await ConnectClientAndEnterPlayingAsync(client, server.GamePort, "client join after second server restart");

        await WaitForServerSessionCountAsync(server, 1, "server did not retain the final client session");
        ThrowIfAnyOwnedContextHasError();
        fixture.ThrowIfAnyContextHasError();
    }

    public void Dispose() {
        for (int i = ownedClients.Count - 1; i >= 0; i--) {
            ownedClients[i].Dispose();
        }

        for (int i = ownedServers.Count - 1; i >= 0; i--) {
            ownedServers[i].Dispose();
        }

        fixture.ThrowIfAnyContextHasError();
    }

    private async Task<GameContext> RestartServerFromOriginalWorldAsync(
        string sourceDirectory,
        string runDirectory,
        string roleName) {
        GameContext server = new(roleName, sourceDirectory, runDirectory);
        ownedServers.Add(server);
        server.Start();
        Assert.True(
            await server.WaitLogAsync("Entered screen \"MainMenu\"", TimeSpan.FromSeconds(60)),
            $"Restarted server did not enter MainMenu. Logs:{server.GetRecentLogText()}");

        await StartFirstWorldAsync(server);
        await AssertSingleWorldExistsAsync(server);
        return server;
    }

    private static async Task StartFirstWorldAsync(GameContext server) {
        await server.PushCommandAsync(
            "EXEC " +
            "WorldsManager.UpdateWorldsList();" +
            "if (WorldsManager.WorldInfos.Count == 0) throw new InvalidOperationException(\"No world to restart.\");" +
            "ScreensManager.SwitchScreen(\"GameLoading\", WorldsManager.WorldInfos[0], null);");

        Assert.True(
            await server.WaitLogAsync("Entered screen \"Game\""),
            $"Server did not enter Game after restarting the saved world. Logs:{server.GetRecentLogText()}");
        Assert.True(
            await WaitForRecentOrFutureLogAsync(server, "Server started", TestTimeouts.LongEventually),
            $"Server network did not start after restarting the saved world. Logs:{server.GetRecentLogText()}");
    }

    private static async Task ConnectClientAndEnterPlayingAsync(GameContext client, int serverPort, string stepName) {
        await client.PushCommandAsync("EXEC DialogsManager.HideAllDialogs();");
        await client.PushCommandAsync(
            "EXEC " +
            GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs")
                .Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        Assert.True(
            await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", TimeSpan.FromSeconds(45)),
            $"{stepName}: client did not connect to server port {serverPort}. Logs:{client.GetRecentLogText()}");

        ClientEntryState state = await WaitForClientEntryStateAsync(client, stepName);
        if (state.Screen == "PlayerScreen") {
            await ClickPlayerPlayButtonAsync(client, stepName);
        }

        ClientEntryState playing = await WaitForClientPlayingAsync(client, stepName);
        Assert.True(
            playing.IsPlaying,
            $"{stepName}: client did not enter Playing state. State={playing.Raw}. SequenceName={MovementSequenceName}. Sequence={FormatMovementSequence(MovementSequence)}. Logs:{client.GetRecentLogText()}");
        await InjectMovementSequenceAsync(client, MovementSequence, stepName);
    }

    private static async Task InjectMovementSequenceAsync(
        GameContext client,
        IReadOnlyList<MovementStep> sequence,
        string stepName) {
        if (EnabledMovementSteps != null && !EnabledMovementSteps.Contains(stepName)) {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return;
        }

        foreach (MovementStep step in sequence) {
            await client.PushCommandAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"MOVE_INPUT {step.X:R} {step.Y:R} {step.Z:R} {step.FrameCount}"));
            await Task.Delay(step.DelayAfterMs);
        }

        if (client.FatalException != null) {
            throw new InvalidOperationException(
                $"{stepName}: movement sequence caused a client fatal error. SequenceName={MovementSequenceName}. Sequence={FormatMovementSequence(sequence)}",
                client.FatalException);
        }
    }

    private static MovementStep[] CreateMovementSequence(string name) {
        MovementStep Hold(float x, float y, float z) => new(x, y, z, HoldMovementFrameCount, HoldMovementDelayAfterMs);
        return name switch {
            "none" => [new(0f, 0f, 0f, 1, 5000)],
            "forward_hold" => [Hold(0f, 0f, 1f)],
            "back_hold" => [Hold(0f, 0f, -1f)],
            "left_hold" => [Hold(-1f, 0f, 0f)],
            "right_hold" => [Hold(1f, 0f, 0f)],
            "forward_right_hold" => [Hold(1f, 0f, 1f)],
            "forward_left_hold" => [Hold(-1f, 0f, 1f)],
            "up_hold" => [Hold(0f, 1f, 0f)],
            "down_hold" => [Hold(0f, -1f, 0f)],
            "vertical_forward_hold" => [Hold(0f, 1f, 1f)],
            "fast_alternating_z" => [
                new(0f, 0f, 1f, 12, 120),
                new(0f, 0f, -1f, 12, 120),
                new(0f, 0f, 1f, 12, 120),
                new(0f, 0f, -1f, 12, 120),
                new(0f, 0f, 1f, 12, 120),
                new(0f, 0f, -1f, 12, 120),
                new(0f, 0f, 1f, 600, 4280)
            ],
            "fast_alternating_x" => [
                new(1f, 0f, 0f, 12, 120),
                new(-1f, 0f, 0f, 12, 120),
                new(1f, 0f, 0f, 12, 120),
                new(-1f, 0f, 0f, 12, 120),
                new(1f, 0f, 0f, 12, 120),
                new(-1f, 0f, 0f, 12, 120),
                new(1f, 0f, 0f, 600, 4280)
            ],
            _ => [
                new(0f, 0f, 1f, 45, 700),
                new(1f, 0f, 0f, 45, 700),
                new(0f, 0f, -1f, 45, 700),
                new(-1f, 0f, 0f, 45, 700),
                new(1f, 0f, 1f, 45, 700),
                new(0f, 0f, 1f, 600, 500)
            ]
        };
    }

    private static int GetEnvironmentInt32(string name, int defaultValue) =>
        int.TryParse(
            Environment.GetEnvironmentVariable(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : defaultValue;

    private static HashSet<string>? CreateEnabledMovementSteps(string? value) {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatMovementSequence(IReadOnlyList<MovementStep> sequence) =>
        string.Join(
            " -> ",
            sequence.Select(step => string.Create(
                CultureInfo.InvariantCulture,
                $"MOVE_INPUT {step.X:R} {step.Y:R} {step.Z:R} {step.FrameCount};wait={step.DelayAfterMs}ms")));

    private static async Task ClickPlayerPlayButtonAsync(GameContext client, string stepName) {
        await TestWaiter.UntilAsync(
            async () => (string?)await client.PushCommandAsync(
                "FUNC " +
                "if (ScreensManager.CurrentScreen is GameScreen) return \"game\";" +
                "var screen = ScreensManager.CurrentScreen as PlayerScreen;" +
                "return screen != null && screen.m_playerData != null && screen.m_playButton.IsVisible && screen.m_playButton.IsEnabled ? \"ready\" : ScreensManager.CurrentScreen?.GetType().Name ?? \"null\";")
                ?? "null",
            state => state == "ready" || state == "game",
            TimeSpan.FromSeconds(45),
            $"{stepName}: PlayerScreen did not become ready.");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
    }

    private static async Task<ClientEntryState> WaitForClientEntryStateAsync(GameContext client, string stepName) {
        return await TestWaiter.UntilAsync(
            () => GetClientEntryStateAsync(client),
            state => state.Screen == "PlayerScreen" || state.IsPlaying,
            TimeSpan.FromSeconds(45),
            $"{stepName}: client did not reach PlayerScreen or GameScreen. SequenceName={MovementSequenceName}. Sequence={FormatMovementSequence(MovementSequence)}.");
    }

    private static async Task<ClientEntryState> WaitForClientPlayingAsync(GameContext client, string stepName) {
        return await TestWaiter.UntilAsync(
            () => GetClientEntryStateAsync(client),
            state => state.IsPlaying,
            TimeSpan.FromSeconds(45),
            $"{stepName}: client did not reach NetworkState.Playing. SequenceName={MovementSequenceName}. Sequence={FormatMovementSequence(MovementSequence)}.");
    }

    private static async Task<ClientEntryState> GetClientEntryStateAsync(GameContext client) {
        string raw = (string?)await client.PushCommandAsync(
            "FUNC " +
            "var screen = ScreensManager.CurrentScreen?.GetType().Name ?? \"null\";" +
            "var projectLoaded = GameManager.Project != null;" +
            "var playerIndex = NetworkManager.PlayerIndex;" +
            "var players = projectLoaded ? GameManager.Project.FindSubsystem<SubsystemPlayers>() : null;" +
            "var playerData = players?.PlayersData.FirstOrDefault(p => p.PlayerIndex == playerIndex);" +
            "var hasPlayerEntity = playerData?.ComponentPlayer != null;" +
            "return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"screen={0};project={1};playerIndex={2};state={3};hasPlayerEntity={4}\", screen, projectLoaded, playerIndex, NetworkManager.MyState, hasPlayerEntity);")
            ?? string.Empty;

        return ClientEntryState.Parse(raw);
    }

    private async Task<GameContext> KillAndRestartClientProcessAsync(GameContext client, string roleName) {
        string sourceDirectory = client.SourceDirectory;
        string runDirectory = client.RunDirectory;
        await KillClientProcessAsync(client);
        await Task.Delay(TimeSpan.FromSeconds(1));

        GameContext restartedClient = new(roleName, sourceDirectory, runDirectory);
        ownedClients.Add(restartedClient);
        restartedClient.Start();
        Assert.True(
            await restartedClient.WaitLogAsync("Entered screen \"MainMenu\"", TimeSpan.FromSeconds(60)),
            $"Restarted client did not enter MainMenu. Logs:{restartedClient.GetRecentLogText()}");
        return restartedClient;
    }

    private static async Task KillClientProcessAsync(GameContext client) {
        object? scheduled = await client.PushCommandAsync(
            "FUNC " +
            "System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(200); System.Diagnostics.Process.GetCurrentProcess().Kill(); });" +
            "return true;");
        Assert.True(scheduled is true, $"Client kill was not scheduled. Result={scheduled}");
    }

    private static async Task SaveAndKillServerProcessAsync(GameContext server) {
        object? scheduled = await server.PushCommandAsync(
            "FUNC " +
            "GameManager.SaveProject(true, true);" +
            "System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(200); System.Diagnostics.Process.GetCurrentProcess().Kill(); });" +
            "return true;");
        Assert.True(scheduled is true, $"Server kill was not scheduled. Result={scheduled}");
    }

    private static async Task WaitForClientTimeoutDisconnectAsync(GameContext client, string stepName) {
        Assert.True(
            await client.WaitLogAsync("Reason: Timeout", NetworkDisconnectTimeout),
            $"{stepName}: client did not time out after server process terminated. Logs:{client.GetRecentLogText()}");
        Assert.True(
            await WaitForRecentOrFutureLogAsync(client, "Dialog shown: Disconnected", TestTimeouts.LongEventually),
            $"{stepName}: client did not show the disconnected dialog. Logs:{client.GetRecentLogText()}");
        Assert.True(
            await WaitForRecentOrFutureLogAsync(client, "Entered screen \"MainMenu\"", TestTimeouts.LongEventually),
            $"{stepName}: client did not return to MainMenu after timeout. Logs:{client.GetRecentLogText()}");
        await WaitForClientNotConnectedAsync(client, $"{stepName}: client network state did not become NotConnected");
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

    private static async Task WaitForClientNotConnectedAsync(GameContext client, string failureMessage) {
        await TestWaiter.UntilAsync(
            async () => (string?)await client.PushCommandAsync("FUNC return NetworkManager.MyState.ToString();") ?? string.Empty,
            state => state == "NotConnected",
            TestTimeouts.DefaultEventually,
            failureMessage);
    }

    private static async Task WaitForServerSessionCountAsync(GameContext server, int expectedCount, string failureMessage) {
        await WaitForServerSessionCountAsync(server, expectedCount, TestTimeouts.DefaultEventually, failureMessage);
    }

    private static async Task WaitForServerSessionCountAsync(
        GameContext server,
        int expectedCount,
        TimeSpan timeout,
        string failureMessage) {
        await TestWaiter.UntilAsync(
            async () => Convert.ToInt32(
                await server.PushCommandAsync("FUNC return NetworkManager.ServerSessions.Count();"),
                CultureInfo.InvariantCulture),
            count => count == expectedCount,
            timeout,
            failureMessage);
    }

    private static async Task AssertSingleWorldExistsAsync(GameContext server) {
        int worldCount = Convert.ToInt32(
            await server.PushCommandAsync("FUNC WorldsManager.UpdateWorldsList(); return WorldsManager.WorldInfos.Count;"),
            CultureInfo.InvariantCulture);
        Assert.Equal(1, worldCount);
    }

    private void ThrowIfAnyOwnedContextHasError() {
        foreach (GameContext context in ownedServers) {
            if (context.FatalException != null) {
                throw context.FatalException;
            }
        }

        foreach (GameContext context in ownedClients) {
            if (context.FatalException != null) {
                throw context.FatalException;
            }
        }
    }

    private readonly record struct ClientEntryState(
        string Screen,
        bool ProjectLoaded,
        int PlayerIndex,
        string NetworkState,
        bool HasPlayerEntity,
        string Raw) {
        public bool IsPlaying =>
            Screen == "GameScreen"
            && ProjectLoaded
            && PlayerIndex >= 0
            && NetworkState == "Playing"
            && HasPlayerEntity;

        public static ClientEntryState Parse(string raw) {
            Dictionary<string, string> values = ParseKeyValues(raw);
            return new ClientEntryState(
                values.GetValueOrDefault("screen", string.Empty),
                ParseBool(values, "project"),
                ParseInt(values, "playerIndex", -1),
                values.GetValueOrDefault("state", string.Empty),
                ParseBool(values, "hasPlayerEntity"),
                raw);
        }

        private static Dictionary<string, string> ParseKeyValues(string raw) {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                string[] pair = part.Split('=', 2);
                if (pair.Length == 2) {
                    values[pair[0]] = pair[1];
                }
            }

            return values;
        }

        private static bool ParseBool(Dictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string? value)
            && bool.TryParse(value, out bool parsed)
            && parsed;

        private static int ParseInt(Dictionary<string, string> values, string key, int fallback) =>
            values.TryGetValue(key, out string? value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
    }

    private readonly record struct MovementStep(
        float X,
        float Y,
        float Z,
        int FrameCount,
        int DelayAfterMs);
}
