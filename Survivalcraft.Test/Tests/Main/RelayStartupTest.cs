using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Game.Network;
using LiteNetLib;
using LiteNetLib.Utils;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class RelayStartupTest : IClassFixture<GameFixture> {
    private static readonly TimeSpan RelayPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly GameFixture fixture;

    public RelayStartupTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task RelayCarriesHandshakeProjectPlayerAndTerrainSynchronization() {
        GameContext server = StartExtraContext("RelayServerGame");
        GameContext client = StartExtraContext("RelayClient");
        int relayPort = GetAvailableUdpPort();
        using Process relay = await StartRelayServerAsync(relayPort);
        try {
            await StartRelayHostAsync(server, relayPort, "relay-e2e");
            int playerIndex = await ConnectRelayClientAsync(client, relayPort, await WaitForRoomCodeAsync(server));

            TerrainSyncTools.TerrainChunkTarget target = await TerrainSyncTools.PaintChunkPatternAsync(server);
            (TerrainSyncTools.TerrainChunkFingerprint expected, TerrainSyncTools.TerrainChunkFingerprint actual) =
                await TerrainSyncTools.WaitForMatchingChunkFingerprintAsync(server, client, target);
            Assert.True(TerrainSyncTools.FingerprintsMatch(expected, actual),
                $"Terrain data did not cross the relay. Server={expected.Raw}, Client={actual.Raw}");

            TerrainSyncTools.TerrainCellState changed = await TerrainSyncTools.ChangePatternCellAsync(client, target);
            TerrainSyncTools.TerrainChunkFingerprint clientAfter = await TerrainSyncTools.GetChunkFingerprintAsync(client, target);
            TerrainSyncTools.TerrainChunkFingerprint serverAfter =
                await TerrainSyncTools.WaitForChunkCellSamplesAsync(server, target, clientAfter);
            Assert.True(TerrainSyncTools.CellSamplesMatch(clientAfter, serverAfter),
                $"Client terrain mutation did not return through relay. Changed={changed.Raw}, Client={clientAfter.Raw}, Server={serverAfter.Raw}");

            fixture.ThrowIfAnyContextHasError();
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            await StopGameAsync(client);
            client.Dispose();
            await StopGameAsync(server);
            server.Dispose();
        }
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task RelayHostDisconnectDropsClientStateAndClearsHostSessions() {
        GameContext server = StartExtraContext("RelayServerGame");
        GameContext client = StartExtraContext("RelayClient");
        int relayPort = GetAvailableUdpPort();
        using Process relay = await StartRelayServerAsync(relayPort);
        try {
            string roomCode = await StartRelayHostAsync(server, relayPort, "relay-host-disconnect");
            int playerIndex = await ConnectRelayClientAsync(client, relayPort, roomCode);
            Assert.True(await WaitForRelaySessionAsync(server, playerIndex),
                "Host did not retain the relay player before disconnect.");

            relay.Kill(true);
            Assert.True(relay.WaitForExit(TestTimeouts.DefaultEventually), "Relay process did not exit after being killed.");

            Assert.True(await WaitForRelayClientDisconnectedAsync(client),
                $"Client relay state did not become disconnected. Logs:{client.GetRecentLogText()}");
            Assert.True(await WaitForRelaySessionCountAsync(server, 0),
                "Host relay sessions were not cleared after relay disconnect.");
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            await StopGameAsync(client);
            client.Dispose();
            await StopGameAsync(server);
            server.Dispose();
        }
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task RelayClientDisconnectRemovesHostSession() {
        GameContext server = StartExtraContext("RelayServerGame");
        GameContext client = StartExtraContext("RelayClient");
        int relayPort = GetAvailableUdpPort();
        using Process relay = await StartRelayServerAsync(relayPort);
        try {
            string roomCode = await StartRelayHostAsync(server, relayPort, "relay-client-disconnect");
            int playerIndex = await ConnectRelayClientAsync(client, relayPort, roomCode);
            Assert.True(await WaitForRelaySessionAsync(server, playerIndex),
                "Host did not retain the relay player before client disconnect.");

            await StopGameAsync(client);

            Assert.True(await WaitForRelaySessionMissingAsync(server, playerIndex),
                "Host did not remove the relay player session after client disconnect.");
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            await StopGameAsync(client);
            client.Dispose();
            await StopGameAsync(server);
            server.Dispose();
        }
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task RelayAllowsTwoClientsToJoinSameRoomAndReceiveTerrain() {
        GameContext server = StartExtraContext("RelayServerGame");
        GameContext client = StartExtraContext("RelayClient");
        GameContext spectator = StartExtraContext("RelaySpectator");
        int relayPort = GetAvailableUdpPort();
        using Process relay = await StartRelayServerAsync(relayPort);
        try {
            string roomCode = await StartRelayHostAsync(server, relayPort, "relay-two-clients");
            int clientIndex = await ConnectRelayClientAsync(client, relayPort, roomCode);
            int spectatorIndex = await ConnectRelayClientAsync(spectator, relayPort, roomCode);

            Assert.NotEqual(clientIndex, spectatorIndex);
            Assert.True(await WaitForRelaySessionCountAsync(server, 2),
                "Host did not retain two relay client sessions.");

            TerrainSyncTools.TerrainChunkTarget target = await TerrainSyncTools.PaintChunkPatternAsync(server);
            (TerrainSyncTools.TerrainChunkFingerprint expected, TerrainSyncTools.TerrainChunkFingerprint clientActual) =
                await TerrainSyncTools.WaitForMatchingChunkFingerprintAsync(server, client, target);
            TerrainSyncTools.TerrainChunkFingerprint spectatorActual =
                await TerrainSyncTools.WaitForChunkCellSamplesAsync(spectator, target, expected);

            Assert.True(TerrainSyncTools.FingerprintsMatch(expected, clientActual),
                $"Client did not receive terrain through relay. Server={expected.Raw}, Client={clientActual.Raw}");
            Assert.True(TerrainSyncTools.CellSamplesMatch(expected, spectatorActual),
                $"Spectator did not receive terrain through relay. Server={expected.Raw}, Spectator={spectatorActual.Raw}");
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            await StopGameAsync(spectator);
            spectator.Dispose();
            await StopGameAsync(client);
            client.Dispose();
            await StopGameAsync(server);
            server.Dispose();
        }
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task RelayRoutesHostPacketsOnlyToSelectedPeer() {
        GameContext server = StartExtraContext("RelayServerGame");
        GameContext client = StartExtraContext("RelayClient");
        GameContext spectator = StartExtraContext("RelaySpectator");
        int relayPort = GetAvailableUdpPort();
        using Process relay = await StartRelayServerAsync(relayPort);
        try {
            string roomCode = await StartRelayHostAsync(server, relayPort, "relay-target-route");
            int clientIndex = await ConnectRelayClientAsync(client, relayPort, roomCode);
            int spectatorIndex = await ConnectRelayClientAsync(spectator, relayPort, roomCode);
            int probeIndex = 9001;

            string routeScript = GameTools.ReadScript("RelayStartup/SendIndexToRelaySessionCode.cs")
                .Replace("__PLAYER_INDEX__", clientIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__TEST_INDEX__", probeIndex.ToString(CultureInfo.InvariantCulture));
            object? routed = await server.PushCommandAsync("FUNC " + routeScript);
            Assert.True(routed is true, "Host could not find the target relay session.");

            Assert.True(await WaitForPlayerIndexAsync(client, probeIndex),
                "Target client did not receive the directed relay packet.");
            await TestWaiter.AssertRemainsAsync(
                () => ReadPlayerIndexAsync(spectator),
                index => index == spectatorIndex,
                TestTimeouts.ShortObservation,
                "Spectator received a directed relay packet intended for another peer.",
                TestTimeouts.PollInterval);
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            await StopGameAsync(spectator);
            spectator.Dispose();
            await StopGameAsync(client);
            client.Dispose();
            await StopGameAsync(server);
            server.Dispose();
        }
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task RelayHostRecreatesSessionWhenPayloadArrivesBeforePeerJoined() {
        GameContext server = StartExtraContext("RelayServerGame");
        int relayPort = GetAvailableUdpPort();
        using Process relay = await StartRelayServerAsync(relayPort);
        try {
            string roomCode = await StartRelayHostAsync(server, relayPort, "relay-first-payload");

            using RelayProbe peerProbe = await RelayProbe.ConnectAsync(relayPort);
            peerProbe.Send(RelayMessageType.JoinRoom, writer => writer.Put(roomCode));
            Assert.True(await peerProbe.WaitForJoinAcceptedAsync(), "Probe peer did not join the relay room.");
            Assert.True(await WaitForRelaySessionCountAsync(server, 1),
                "Host did not receive the probe peer join.");

            object? cleared = await server.PushCommandAsync(
                "FUNC " + GameTools.ReadScript("RelayStartup/ClearRelayHostSessionsCode.cs"));
            Assert.True(cleared is true, "Failed to clear host relay sessions for the missing PeerJoined simulation.");
            Assert.True(await WaitForRelaySessionCountAsync(server, 0),
                "Host relay sessions were not cleared before the first-payload probe.");

            peerProbe.SendDataToHostPayload([1, 2, 3]);

            Assert.True(await WaitForRelaySessionCountAtLeastAsync(server, 1),
                "Host did not recreate a relay session from an incoming payload.");
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            await StopGameAsync(server);
            server.Dispose();
        }
    }

    [Fact, TestOrder(Priority = 7)]
    public async Task RelayServerSurvivesInvalidProtocolFramesAndOversizedPayloads() {
        GameContext server = StartExtraContext("RelayServerGame");
        GameContext client = StartExtraContext("RelayClient");
        int relayPort = GetAvailableUdpPort();
        using Process relay = await StartRelayServerAsync(relayPort);
        try {
            using RelayProbe hostProbe = await RelayProbe.ConnectAsync(relayPort);
            hostProbe.Send(RelayMessageType.CreateRoom);
            string probeRoomCode = await hostProbe.WaitForRoomCodeAsync();

            using RelayProbe peerProbe = await RelayProbe.ConnectAsync(relayPort);
            peerProbe.Send(RelayMessageType.JoinRoom, writer => writer.Put(probeRoomCode));
            Assert.True(await peerProbe.WaitForJoinAcceptedAsync(), "Probe peer did not join the relay room.");

            peerProbe.SendInvalidHeader();
            peerProbe.SendOversizedDataToHost();
            await TestWaiter.AssertRemainsAsync(
                () => Task.FromResult(relay.HasExited),
                exited => !exited,
                TestTimeouts.ShortObservation,
                "Relay server exited after invalid protocol frames.",
                RelayPollInterval);

            string roomCode = await StartRelayHostAsync(server, relayPort, "relay-invalid-frames");
            int playerIndex = await ConnectRelayClientAsync(client, relayPort, roomCode);
            Assert.True(await WaitForRelaySessionAsync(server, playerIndex),
                "Relay server did not remain usable after invalid protocol frames.");
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            await StopGameAsync(client);
            client.Dispose();
            await StopGameAsync(server);
            server.Dispose();
        }
    }

    [Fact, TestOrder(Priority = 8)]
    public async Task PlayWithFriendsCreatesCenterRelayRoomShowsHostCodeAndAcceptsRelayPeer() {
        GameContext server = fixture.Server;
        int centerPort = GetAvailableTcpPort();
        int relayPort = GetAvailableUdpPort();
        string centerUrl = $"http://127.0.0.1:{centerPort.ToString(CultureInfo.InvariantCulture)}";
        string relayId = $"relay-e2e-{Guid.NewGuid():N}";

        using Process center = await StartCenterServerAsync(centerPort);
        using Process relay = await StartRelayServerAsync(relayPort, centerUrl, relayId);
        try {
            Assert.True(await WaitForRelayCandidateAsync(centerUrl, relayId),
                $"Center server did not expose the relay candidate. Center={centerUrl}, RelayId={relayId}");

            await ConfigurePlayWithFriendsAsync(server, centerUrl, askBeforeEntering: true, enabled: false);
            await CreateWorldThroughPlayWithFriendsDialogAsync(server, "center-relay-ui");
            await ClickPlayerPlayButtonIfShownAsync(server);

            RelayDisplayState hostDisplay = await WaitForRelayDisplayStateAsync(server, s =>
                !string.IsNullOrWhiteSpace(s.DisplayCode)
                && s.LabelVisible
                && s.LabelText.Contains(s.DisplayCode, StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(hostDisplay.DisplayCode),
                $"Host did not receive a global relay code. State={hostDisplay.Raw}");
            Assert.True(hostDisplay.LabelVisible, $"Host relay code label was not visible. State={hostDisplay.Raw}");
            Assert.Contains(hostDisplay.DisplayCode, hostDisplay.LabelText, StringComparison.Ordinal);

            Assert.True(await CenterRoomLookupContainsAsync(centerUrl, hostDisplay.DisplayCode, relayId, hostDisplay.RoomCode),
                $"Center did not resolve global code {hostDisplay.DisplayCode} to relay={relayId}, room={hostDisplay.RoomCode}.");

            using RelayProbe peerProbe = await RelayProbe.ConnectAsync(relayPort);
            peerProbe.Send(RelayMessageType.JoinRoom, writer => writer.Put(hostDisplay.RoomCode));
            Assert.True(await peerProbe.WaitForJoinAcceptedAsync(), $"Probe peer did not join room {hostDisplay.RoomCode}.");
            Assert.True(await WaitForRelaySessionCountAtLeastAsync(server, 1),
                $"Host did not retain a peer that joined the displayed relay room. Host={hostDisplay.Raw}");
        }
        finally {
            if (!relay.HasExited) relay.Kill(true);
            if (!center.HasExited) center.Kill(true);
            await StopGameAsync(server);
        }
    }

    private static async Task<string> StartRelayHostAsync(GameContext server, int relayPort, string seed) {
        await GameTools.CreateTestWorld(server, seed);

        string startHostScript = GameTools.ReadScript("RelayStartup/StartRelayHostCode.cs")
            .Replace("__RELAY_PORT__", relayPort.ToString(CultureInfo.InvariantCulture));
        await server.PushCommandAsync("FUNC " + startHostScript);

        string roomCode = await WaitForRoomCodeAsync(server);
        Assert.False(string.IsNullOrWhiteSpace(roomCode), "Relay did not allocate a room code.");
        return roomCode;
    }

    private static async Task ConfigurePlayWithFriendsAsync(GameContext context, string centerUrl, bool askBeforeEntering, bool enabled) {
        await context.PushCommandAsync(
            "EXEC " +
            $"SettingsManager.CenterServerUrl = \"{EscapeScriptString(centerUrl)}\";" +
            $"SettingsManager.ShowPlayWithFriendsDialog = {FormatBool(askBeforeEntering)};" +
            $"SettingsManager.PlayWithFriendsEnabled = {FormatBool(enabled)};" +
            "SettingsManager.SaveSettings();");
    }

    private static async Task CreateWorldThroughPlayWithFriendsDialogAsync(GameContext server, string seed) {
        await server.PushCommandAsync("CLICK_WIDGET Play");
        Assert.True(await server.WaitLogAsync("Entered screen \"Play\""),
            $"Failed to enter Play screen. Logs:{server.GetRecentLogText()}");

        await server.PushCommandAsync("CLICK_WIDGET NewWorld");
        Assert.True(await server.WaitLogAsync("Entered screen \"NewWorld\""),
            $"Failed to enter NewWorld screen. Logs:{server.GetRecentLogText()}");

        await WaitForNewWorldSettingsAsync(server);
        await server.PushCommandAsync($"EXEC (ScreensManager.CurrentScreen as NewWorldScreen).m_seedTextBox.Text = \"{EscapeScriptString(seed)}\";");
        await server.PushCommandAsync("CLICK_WIDGET Play");

        Assert.True(await WaitForPlayWithFriendsDialogAsync(server),
            $"Play-with-friends dialog did not appear. Logs:{server.GetRecentLogText()}");
        await server.PushCommandAsync("CLICK_WIDGET PlayWithFriendsDialog.Yes");
        Assert.True(await WaitForGameScreenAsync(server),
            $"Host did not enter Game after choosing play-with-friends. Logs:{server.GetRecentLogText()}");
    }

    private static async Task WaitForNewWorldSettingsAsync(GameContext context) {
        await TestWaiter.UntilAsync(
            () => context.PushCommandAsync("FUNC return (ScreensManager.CurrentScreen as NewWorldScreen)?.m_worldSettings != null;"),
            ready => ready is true,
            TestTimeouts.LongEventually,
            $"NewWorld settings were not ready. Logs:{context.GetRecentLogText()}",
            TestTimeouts.PollInterval);
    }

    private static async Task<bool> WaitForPlayWithFriendsDialogAsync(GameContext context) {
        WaitResult<object?> result = await TestWaiter.TryUntilAsync(
            () => context.PushCommandAsync("FUNC return DialogsManager.Dialogs.Any(d => d is PlayWithFriendsDialog);"),
            shown => shown is true,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private static async Task<bool> WaitForGameScreenAsync(GameContext context) {
        WaitResult<object?> result = await TestWaiter.TryUntilAsync(
            () => context.PushCommandAsync("FUNC return ScreensManager.CurrentScreen is GameScreen && GameManager.Project != null;"),
            ready => ready is true,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private GameContext StartExtraContext(string roleName) {
        GameContext context = fixture.CreateContext($"{roleName}-{Guid.NewGuid():N}");
        context.Start();
        Assert.True(context.WaitLog("Entered screen \"MainMenu\""),
            $"{roleName} did not enter MainMenu. Logs:{context.GetRecentLogText()}");
        return context;
    }

    private static async Task<int> ConnectRelayClientAsync(GameContext client, int relayPort, string roomCode) {
        string connectScript = GameTools.ReadScript("RelayStartup/ConnectRelayCode.cs")
            .Replace("__RELAY_PORT__", relayPort.ToString(CultureInfo.InvariantCulture))
            .Replace("__ROOM_CODE__", roomCode);
        await client.PushCommandAsync("FUNC " + connectScript);

        Assert.True(await client.WaitLogAsync("Entered screen \"Player\""),
            $"Relay client did not receive and load Project data. Client logs:{client.GetRecentLogText()}");
        await ClickPlayerPlayButtonAsync(client);
        Assert.True(await WaitForClientPlayingAsync(client),
            $"Relay client did not enter playing state. Logs:{client.GetRecentLogText()}");

        RelayClientPlayingState playingState = await WaitForClientPlayingStateAsync(client);
        Assert.True(playingState.IsPlaying,
            $"Relay client did not reach a concrete playing runtime state. State={playingState.Raw}. Logs:{client.GetRecentLogText()}");
        await AssertClientHostLatencyAsync(client, "through relay room code");

        return playingState.PlayerIndex;
    }

    private static async Task StopGameAsync(GameContext context) {
        if (!context.IsStarted) return;
        try {
            await context.PushCommandAsync("EXEC " + GameTools.ReadScript("RelayStartup/StopNetworkAndReturnMainMenuCode.cs"));
            await context.WaitLogAsync("Entered screen \"MainMenu\"", 5);
        }
        catch {
            // Cleanup must not hide the original test failure.
        }
    }

    private static async Task<string> WaitForRoomCodeAsync(GameContext server) {
        return await TestWaiter.UntilAsync(
            async () => (string?)await server.PushCommandAsync("FUNC return Game.NetworkManager.RelayRoomCode;") ?? string.Empty,
            code => !string.IsNullOrWhiteSpace(code),
            TestTimeouts.LongEventually,
            $"Relay did not allocate a room code. Logs:{server.GetRecentLogText()}",
            TestTimeouts.PollInterval);
    }

    private static async Task<bool> WaitForRelayClientDisconnectedAsync(GameContext client) {
        WaitResult<object?> result = await TestWaiter.TryUntilAsync(
            () => client.PushCommandAsync(
                "FUNC return !Game.NetworkManager.IsClientRunning || Game.NetworkManager.MyState == Game.NetworkState.NotConnected;"),
            result => result is true,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private static async Task<bool> WaitForRelaySessionAsync(GameContext server, int playerIndex) {
        WaitResult<object?> result = await TestWaiter.TryUntilAsync(
            () => server.PushCommandAsync(
                $"FUNC return Game.NetworkManager.ServerSessions.Any(s => s.PlayerIndex == {playerIndex.ToString(CultureInfo.InvariantCulture)});"),
            exists => exists is true,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private static async Task<bool> WaitForRelaySessionMissingAsync(GameContext server, int playerIndex) {
        WaitResult<object?> result = await TestWaiter.TryUntilAsync(
            () => server.PushCommandAsync(
                $"FUNC return !Game.NetworkManager.ServerSessions.Any(s => s.PlayerIndex == {playerIndex.ToString(CultureInfo.InvariantCulture)});"),
            missing => missing is true,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private static Task<bool> WaitForRelaySessionCountAsync(GameContext server, int expected) =>
        WaitForRelaySessionCountAsync(server, count => count == expected);

    private static Task<bool> WaitForRelaySessionCountAtLeastAsync(GameContext server, int expectedMinimum) =>
        WaitForRelaySessionCountAsync(server, count => count >= expectedMinimum);

    private static async Task<bool> WaitForRelaySessionCountAsync(GameContext server, Func<int, bool> predicate) {
        WaitResult<int> result = await TestWaiter.TryUntilAsync(
            async () => Convert.ToInt32(await server.PushCommandAsync("FUNC return Game.NetworkManager.ServerSessions.Count();"), CultureInfo.InvariantCulture),
            predicate,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private static async Task AssertClientHostLatencyAsync(GameContext client, string connectionDescription) {
        int latency = await WaitForClientHostLatencyAsync(client);
        Assert.True(latency >= 0,
            $"Relay client did not receive host latency {connectionDescription}. Last latency={latency}. Logs:{client.GetRecentLogText()}");
    }

    private static async Task<int> WaitForClientHostLatencyAsync(GameContext client) {
        WaitResult<int> result = await TestWaiter.TryUntilAsync(
            () => ReadClientHostLatencyAsync(client),
            latency => latency >= 0,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched ? result.Last : -1;
    }

    private static async Task<bool> WaitForPlayerIndexAsync(GameContext context, int expected) {
        WaitResult<int> result = await TestWaiter.TryUntilAsync(
            () => ReadPlayerIndexAsync(context),
            playerIndex => playerIndex == expected,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private static async Task<int> ReadPlayerIndexAsync(GameContext context) {
        object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> ReadClientHostLatencyAsync(GameContext client) {
        object? result = await client.PushCommandAsync("FUNC return Game.NetworkManager.ClientLatency;");
        return result is int value ? value : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task ClickPlayerPlayButtonAsync(GameContext context) {
        WaitResult<string> state = await TestWaiter.TryUntilAsync(
            async () => (string?)await context.PushCommandAsync(
                "FUNC if (ScreensManager.CurrentScreen is GameScreen) return \"game\";" +
                "var screen = ScreensManager.CurrentScreen as PlayerScreen;" +
                "return screen != null && screen.m_playerData != null && screen.m_playButton.IsVisible && screen.m_playButton.IsEnabled ? \"ready\" : \"wait\";")
                ?? "wait",
            text => string.Equals(text, "game", StringComparison.Ordinal) || string.Equals(text, "ready", StringComparison.Ordinal),
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        if (string.Equals(state.Last, "game", StringComparison.Ordinal)) {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++) {
            await context.PushCommandAsync("CLICK_WIDGET PlayButton");
            WaitResult<object?> accepted = await TestWaiter.TryUntilAsync(
                () => context.PushCommandAsync(
                    "FUNC return ScreensManager.CurrentScreen is GameScreen || DialogsManager.Dialogs.Any(d => d is BusyDialog);"),
                value => value is true,
                TestTimeouts.ShortObservation,
                TestTimeouts.PollInterval);
            if (accepted.Matched) {
                return;
            }
        }
    }

    private static async Task ClickPlayerPlayButtonIfShownAsync(GameContext context) {
        WaitResult<string> result = await TestWaiter.TryUntilAsync(
            async () => (string?)await context.PushCommandAsync(
                "FUNC if (ScreensManager.CurrentScreen is GameScreen) return \"game\";" +
                "var screen = ScreensManager.CurrentScreen as PlayerScreen;" +
                "if (screen == null) return ScreensManager.CurrentScreen?.GetType().Name ?? \"null\";" +
                "return screen.m_playerData != null && screen.m_playButton.IsVisible && screen.m_playButton.IsEnabled ? \"ready\" : \"player\";")
                ?? "null",
            state => string.Equals(state, "ready", StringComparison.Ordinal) || string.Equals(state, "game", StringComparison.Ordinal),
            TimeSpan.FromSeconds(20),
            RelayPollInterval);

        if (string.Equals(result.Last, "ready", StringComparison.Ordinal)) {
            await ClickPlayerPlayButtonAsync(context);
            await WaitForGameScreenAsync(context);
            return;
        }

        if (!string.Equals(result.Last, "game", StringComparison.Ordinal)) {
            throw new TimeoutException($"Player screen did not become ready. Logs:{context.GetRecentLogText()}");
        }
    }

    private static async Task<bool> WaitForClientPlayingAsync(GameContext context) {
        WaitResult<object?> result = await TestWaiter.TryUntilAsync(
            () => context.PushCommandAsync(
                "FUNC return Game.NetworkManager.PlayerIndex >= 0 && Game.NetworkManager.MyState == Game.NetworkState.Playing;"),
            playing => playing is true,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Matched;
    }

    private static async Task<RelayDisplayState> WaitForRelayDisplayStateAsync(GameContext context, Func<RelayDisplayState, bool> predicate) {
        WaitResult<RelayDisplayState> result = await TestWaiter.TryUntilAsync(
            () => GetRelayDisplayStateAsync(context),
            predicate,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Last;
    }

    private static async Task<RelayDisplayState> GetRelayDisplayStateAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " +
            "var game = ScreensManager.FindScreen<GameScreen>(\"Game\");" +
            "var label = game?.Children.Find<LabelWidget>(\"RelayCodeLabel\", false);" +
            "return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"screen={0};room={1};display={2};labelVisible={3};labelText={4}\"," +
            "ScreensManager.CurrentScreen?.GetType().Name ?? \"null\"," +
            "Game.NetworkManager.RelayRoomCode ?? string.Empty," +
            "Game.NetworkManager.RelayGlobalCode ?? string.Empty," +
            "label?.IsVisible ?? false," +
            "(label?.Text ?? string.Empty).Replace(\";\", \",\"));")
            ?? string.Empty;

        Dictionary<string, string> values = raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        return new RelayDisplayState(
            values.TryGetValue("screen", out string? screen) ? screen : string.Empty,
            values.TryGetValue("room", out string? room) ? room : string.Empty,
            values.TryGetValue("display", out string? display) ? display : string.Empty,
            values.TryGetValue("labelVisible", out string? visible) && bool.TryParse(visible, out bool isVisible) && isVisible,
            values.TryGetValue("labelText", out string? labelText) ? labelText : string.Empty,
            raw);
    }

    private static async Task<RelayClientPlayingState> WaitForClientPlayingStateAsync(GameContext context) {
        WaitResult<RelayClientPlayingState> result = await TestWaiter.TryUntilAsync(
            async () => {
                string raw = (string?)await context.PushCommandAsync(
                "FUNC var screen = ScreensManager.CurrentScreen?.GetType().Name ?? \"null\";" +
                "var projectLoaded = GameManager.Project != null;" +
                "var playerIndex = Game.NetworkManager.PlayerIndex;" +
                "var players = projectLoaded ? GameManager.Project.FindSubsystem<SubsystemPlayers>() : null;" +
                "var playerData = players?.PlayersData.FirstOrDefault(p => p.PlayerIndex == playerIndex);" +
                "var componentPlayer = playerData?.ComponentPlayer;" +
                "var componentPlayerReady = componentPlayer != null;" +
                "var componentMinerReady = componentPlayer?.ComponentMiner != null;" +
                "var componentGuiReady = componentPlayer?.ComponentGui != null;" +
                "var componentBodyReady = componentPlayer?.ComponentBody != null;" +
                "var playerSummaries = players == null ? string.Empty : string.Join(\"|\", players.PlayersData.Select(p => string.Format(System.Globalization.CultureInfo.InvariantCulture, \"idx:{0},main:{1},hasPlayer:{2},miner:{3},state:{4}\", p.PlayerIndex, p.IsMainPlayer, p.ComponentPlayer != null, p.ComponentPlayer?.ComponentMiner != null, p.m_stateMachine.CurrentState ?? string.Empty)));" +
                "return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"screen={0};project={1};playerIndex={2};componentPlayerReady={3};componentMinerReady={4};componentGuiReady={5};componentBodyReady={6};state={7};players={8}\", screen, projectLoaded, playerIndex, componentPlayerReady, componentMinerReady, componentGuiReady, componentBodyReady, Game.NetworkManager.MyState, playerSummaries);")
                ?? string.Empty;
                return RelayClientPlayingState.Parse(raw);
            },
            state => state.IsPlaying,
            TestTimeouts.LongEventually,
            TestTimeouts.PollInterval);
        return result.Last;
    }

    private readonly record struct RelayClientPlayingState(
        string Screen,
        bool ProjectLoaded,
        int PlayerIndex,
        bool ComponentPlayerReady,
        bool ComponentMinerReady,
        string NetworkState,
        string Raw) {
        public bool IsPlaying =>
            Screen == "GameScreen"
            && ProjectLoaded
            && PlayerIndex >= 0
            && ComponentPlayerReady
            && ComponentMinerReady
            && NetworkState == "Playing";

        public static RelayClientPlayingState Parse(string raw) {
            Dictionary<string, string> values = raw
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
            return new RelayClientPlayingState(
                values.TryGetValue("screen", out string? screen) ? screen : string.Empty,
                values.TryGetValue("project", out string? project) && bool.TryParse(project, out bool projectLoaded) && projectLoaded,
                values.TryGetValue("playerIndex", out string? index) && int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerIndex) ? playerIndex : -1,
                values.TryGetValue("componentPlayerReady", out string? ready) && bool.TryParse(ready, out bool componentReady) && componentReady,
                values.TryGetValue("componentMinerReady", out string? minerReady) && bool.TryParse(minerReady, out bool componentMinerReady) && componentMinerReady,
                values.TryGetValue("state", out string? state) ? state : string.Empty,
                raw);
        }
    }

    private readonly record struct RelayDisplayState(
        string Screen,
        string RoomCode,
        string DisplayCode,
        bool LabelVisible,
        string LabelText,
        string Raw);

    private static async Task<Process> StartCenterServerAsync(int port) {
        string centerAssembly = Path.Combine(AppContext.BaseDirectory, "Survivalcraft.CenterServer.dll");
        Assert.True(File.Exists(centerAssembly), $"Center server assembly was not copied to test output: {centerAssembly}");
        string url = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";
        ProcessStartInfo info = new("dotnet") {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add(centerAssembly);
        info.ArgumentList.Add("--urls");
        info.ArgumentList.Add(url);

        Process process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start center server process.");
        try {
            Assert.True(await WaitForHttpOkAsync($"{url}/health"),
                $"Center server did not become healthy at {url}.");
            return process;
        }
        catch {
            if (!process.HasExited) process.Kill(true);
            process.Dispose();
            throw;
        }
    }

    private static async Task<Process> StartRelayServerAsync(int port, string? centerUrl = null, string? relayId = null) {
        string relayAssembly = Path.Combine(AppContext.BaseDirectory, "Survivalcraft.RelayServer.dll");
        Assert.True(File.Exists(relayAssembly), $"Relay server assembly was not copied to test output: {relayAssembly}");
        ProcessStartInfo info = new("dotnet") {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add(relayAssembly);
        info.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(centerUrl)) {
            info.Environment["SC_CENTER_URL"] = centerUrl;
            info.Environment["SC_RELAY_PUBLIC_HOST"] = "127.0.0.1";
            info.Environment["SC_RELAY_PUBLIC_UDP_PORT"] = port.ToString(CultureInfo.InvariantCulture);
            info.Environment["SC_RELAY_ID"] = relayId ?? $"127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";
        }
        Process process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start relay server process.");
        await TestWaiter.AssertRemainsAsync(
            () => Task.FromResult(process.HasExited),
            exited => !exited,
            TestTimeouts.ShortObservation,
            "Relay server exited during startup.",
            RelayPollInterval);
        return process;
    }

    private static async Task<bool> WaitForRelayCandidateAsync(string centerUrl, string relayId) {
        WaitResult<bool> result = await TestWaiter.TryUntilAsync(
            async () => {
                try {
                    using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
                    string json = await client.GetStringAsync($"{centerUrl}/api/v1/relays/candidates");
                    return json.Contains(relayId, StringComparison.OrdinalIgnoreCase);
                }
                catch {
                    // Retry until the center and relay heartbeat are both visible.
                    return false;
                }
            },
            found => found,
            TestTimeouts.PollInterval,
            RelayPollInterval);
        return result.Matched;
    }

    private static async Task<bool> CenterRoomLookupContainsAsync(string centerUrl, string globalCode, string relayId, string relayRoomCode) {
        try {
            using HttpClient client = new() { Timeout = TestTimeouts.DefaultEventually };
            string json = await client.GetStringAsync($"{centerUrl}/api/v1/rooms/{Uri.EscapeDataString(globalCode)}");
            return json.Contains(relayId, StringComparison.OrdinalIgnoreCase)
                && json.Contains(relayRoomCode, StringComparison.OrdinalIgnoreCase);
        }
        catch {
            return false;
        }
    }

    private static async Task<bool> WaitForHttpOkAsync(string url) {
        WaitResult<bool> result = await TestWaiter.TryUntilAsync(
            async () => {
                try {
                    using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
                    using HttpResponseMessage response = await client.GetAsync(url);
                    return response.IsSuccessStatusCode;
                }
                catch {
                    // Retry until process startup finishes.
                    return false;
                }
            },
            ok => ok,
            TestTimeouts.PollInterval,
            RelayPollInterval);
        return result.Matched;
    }

    private static int GetAvailableUdpPort() {
        using UdpClient socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static int GetAvailableTcpPort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string EscapeScriptString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string FormatBool(bool value) => value ? "true" : "false";

    private sealed class RelayProbe : INetEventListener, IDisposable {
        private readonly NetManager manager;
        private readonly ConcurrentQueue<string> roomCodes = new();
        private volatile bool joinAccepted;
        private NetPeer? peer;

        private RelayProbe() {
            manager = new NetManager(this) { DisconnectTimeout = 5000, UpdateTime = 15 };
        }

        public static async Task<RelayProbe> ConnectAsync(int port) {
            RelayProbe probe = new();
            probe.manager.Start();
            probe.manager.Connect(new IPEndPoint(IPAddress.Loopback, port), RelayProtocol.ConnectionKey);
            bool connected = await probe.WaitUntilAsync(() => probe.peer != null);
            Assert.True(connected, "Probe failed to connect to relay server.");
            return probe;
        }

        public void Send(RelayMessageType type, Action<NetDataWriter>? writeBody = null) {
            NetDataWriter writer = RelayProtocol.Begin(type);
            writeBody?.Invoke(writer);
            peer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendInvalidHeader() {
            NetDataWriter writer = new();
            writer.Put(0x12345678u);
            writer.Put(RelayProtocol.Version);
            writer.Put((byte)RelayMessageType.DataToHost);
            peer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendOversizedDataToHost() {
            NetDataWriter writer = RelayProtocol.Begin(RelayMessageType.DataToHost);
            writer.PutBytesWithLength(new byte[RelayProtocol.MaxPayloadLength + 1]);
            peer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendDataToHostPayload(byte[] payload) {
            NetDataWriter writer = RelayProtocol.Begin(RelayMessageType.DataToHost);
            writer.PutBytesWithLength(payload);
            peer?.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public async Task<string> WaitForRoomCodeAsync() {
            bool received = await WaitUntilAsync(() => !roomCodes.IsEmpty);
            Assert.True(received, "Probe host did not receive a room code.");
            Assert.True(roomCodes.TryDequeue(out string? roomCode), "Probe room code queue was empty.");
            return roomCode;
        }

        public Task<bool> WaitForJoinAcceptedAsync() => WaitUntilAsync(() => joinAccepted);

        public void Dispose() {
            manager.Stop();
        }

        public void OnPeerConnected(NetPeer peer) => this.peer = peer;
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) => this.peer = null;
        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
            if (!RelayProtocol.TryReadHeader(reader, out RelayMessageType type)) return;
            if (type == RelayMessageType.RoomCreated) roomCodes.Enqueue(reader.GetString());
            else if (type == RelayMessageType.JoinAccepted) joinAccepted = true;
        }

        private async Task<bool> WaitUntilAsync(Func<bool> predicate) {
            WaitResult<bool> result = await TestWaiter.TryUntilAsync(
                () => {
                    manager.PollEvents();
                    return Task.FromResult(predicate());
                },
                matched => matched,
                TestTimeouts.DefaultEventually,
                TestTimeouts.PollInterval);
            return result.Matched;
        }
    }
}
