using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public sealed class PlayerCreateDuplicateDiagnosticTest : IClassFixture<GameFixture>, IDisposable {
    private const string InstallDiagnosticsCode = """
        var oldConnectServer = NetworkManager.ConnectServerAction;
        NetworkManager.ConnectServerAction = endpoint => {
            Log.Information("[PlayerCreateDiag] ConnectServerAction " + endpoint);
            oldConnectServer(endpoint);
        };
        var oldConnectRelay = NetworkManager.ConnectRelayAction;
        NetworkManager.ConnectRelayAction = (endpoint, roomCode) => {
            Log.Information("[PlayerCreateDiag] ConnectRelayAction " + endpoint + " room=" + roomCode);
            oldConnectRelay(endpoint, roomCode);
        };
        var oldQueue = NetworkManager.QueueAction;
        NetworkManager.QueueAction = packet => {
            if (packet != null && packet.GetType().FullName == "Game.Network.PlayerCreatePacket") {
                object client = NetworkManager.NetworkClient;
                object relay = NetworkManager.RelayClient;
                Log.Information("[PlayerCreateDiag] Queue PlayerCreate client="
                    + (client == null ? "null" : client.GetType().FullName + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(client)
                        + ":connected=" + client.GetType().GetProperty("IsConnected")?.GetValue(client))
                    + " relay="
                    + (relay == null ? "null" : relay.GetType().FullName + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(relay)
                        + ":connected=" + relay.GetType().GetProperty("IsConnected")?.GetValue(relay)));
            }
            oldQueue(packet);
        };
        Log.Information("[PlayerCreateDiag] installed");
        """;

    private const string SnapshotCode = """
        var hook = NetworkManager.NetworkClient != null
            && ModsManager.ModHooks.TryGetValue("OnNetworkPlayerCreate", out var hookValue)
            ? hookValue
            : null;
        string loaders = hook == null
            ? "none"
            : string.Join(",", hook.Loaders.Select(loader => loader.GetType().FullName + "#"
                + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(loader)));
        object client = NetworkManager.NetworkClient;
        object relay = NetworkManager.RelayClient;
        var players = GameManager.Project?.FindSubsystem<SubsystemPlayers>();
        string sessions = string.Join(",", NetworkManager.ServerSessions.Select(session =>
            session.PlayerIndex + ":" + session.NetworkGuid + ":" + session.NetworkState));
        return string.Join(";",
            "hookCount=" + (hook?.Loaders.Count ?? 0),
            "loaders=" + loaders,
            "modLoaderCount=" + ModsManager.ModLoaders.Count,
            "networkClient=" + (client == null ? "null" : client.GetType().FullName + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(client)),
            "networkClientConnected=" + (client?.GetType().GetProperty("IsConnected")?.GetValue(client) ?? "null"),
            "relayClient=" + (relay == null ? "null" : relay.GetType().FullName + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(relay)),
            "relayClientConnected=" + (relay?.GetType().GetProperty("IsConnected")?.GetValue(relay) ?? "null"),
            "state=" + NetworkManager.MyState,
            "playerIndex=" + NetworkManager.PlayerIndex,
            "playerDataCount=" + (players?.PlayersData.Count ?? -1),
            "componentPlayerCount=" + (players?.ComponentPlayers.Count ?? -1),
            "sessions=" + sessions);
        """;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public PlayerCreateDuplicateDiagnosticTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact]
    public async Task DiagnoseDuplicatePlayerCreateRequest() {
        await GameTools.CreateTestWorld(server, "player-create-duplicate-diagnostic");
        await client.PushCommandAsync("EXEC " + InstallDiagnosticsCode);
        await client.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", server.GamePort.ToString()));

        Assert.True(await client.WaitLogAsync($"Connected: 127.0.0.1:{server.GamePort}"), client.GetRecentLogText());
        Assert.True(await client.WaitLogAsync("Entered screen \"Player\""), client.GetRecentLogText());

        string beforeClick = await SnapshotAsync(client);
        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        Assert.True(await client.WaitLogAsync("Player into playing."), client.GetRecentLogText());
        await Task.Delay(1000);

        string afterClick = await SnapshotAsync(client);
        string serverSnapshot = await SnapshotAsync(server);
        string clientLog = client.GetRecentLogText(500);
        int playerCreateQueueCount = Count(clientLog, "[Information] [PlayerCreateDiag] Queue PlayerCreate");
        string diagnosis = Diagnose(beforeClick, afterClick, serverSnapshot, clientLog, playerCreateQueueCount);

        Assert.True(
            playerCreateQueueCount == 1,
            diagnosis + Environment.NewLine + "Client log:" + Environment.NewLine + clientLog);
        fixture.ThrowIfAnyContextHasError();
    }

    public void Dispose() => fixture.ThrowIfAnyContextHasError();

    private static async Task<string> SnapshotAsync(GameContext context) {
        object? result = await context.PushCommandAsync("FUNC " + SnapshotCode);
        return result?.ToString() ?? "<null snapshot>";
    }

    private static string Diagnose(
        string beforeClick,
        string afterClick,
        string serverSnapshot,
        string clientLog,
        int queueCount) {
        int hookCount = ReadInt(beforeClick, "hookCount");
        int componentCount = ReadInt(serverSnapshot, "componentPlayerCount");
        string reason = hookCount > 1
            ? "重复 Loader/Hook 注册：OnNetworkPlayerCreate Loader 数量为 " + hookCount + "。"
            : queueCount > 1
                ? "单个 Loader 在一次自动点击后产生了多次 PlayerCreate 入队。"
                : "本次单击未复现重复 PlayerCreate。";
        return string.Join(
            Environment.NewLine,
            "[PlayerCreateDiag] " + reason,
            "[PlayerCreateDiag] oneInjectedClick=true",
            "[PlayerCreateDiag] playerCreateQueueCount=" + queueCount,
            "[PlayerCreateDiag] serverComponentPlayerCount=" + componentCount,
            "[PlayerCreateDiag] beforeClick=" + beforeClick,
            "[PlayerCreateDiag] afterClick=" + afterClick,
            "[PlayerCreateDiag] server=" + serverSnapshot,
            "[PlayerCreateDiag] connectServerLogCount=" + Count(clientLog, "[Information] [PlayerCreateDiag] ConnectServerAction"),
            "[PlayerCreateDiag] connectRelayLogCount=" + Count(clientLog, "[Information] [PlayerCreateDiag] ConnectRelayAction"));
    }

    private static int ReadInt(string snapshot, string fieldName) {
        string prefix = fieldName + "=";
        int start = snapshot.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return -1;
        start += prefix.Length;
        int end = snapshot.IndexOf(';', start);
        string value = end < 0 ? snapshot[start..] : snapshot[start..end];
        return int.TryParse(value, out int result) ? result : -1;
    }

    private static int Count(string text, string value) {
        int count = 0;
        int start = 0;
        while (!string.IsNullOrEmpty(text) && (start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0) {
            count++;
            start += value.Length;
        }
        return count;
    }
}
