using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Processes;

namespace Survivalcraft.Testing.Scenarios;

public static class MultiplayerScenarioOperations {
    public static Task<NodeStatus> WaitForMainMenuAsync(
        GameNodeProcess node,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        node.WaitForStatusAsync(
            status => status.ContentLoaded
                && string.Equals(status.Screen, "MainMenuScreen", StringComparison.Ordinal),
            timeout,
            "Game content did not reach MainMenu.",
            cancellationToken);

    public static Task<NodeStatus> WaitForWorldEntryAsync(
        GameNodeProcess node,
        bool serverSide,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        node.WaitForStatusAsync(
            status => string.Equals(status.Screen, "PlayerScreen", StringComparison.Ordinal)
                || (status.ProjectLoaded
                    && string.Equals(status.Screen, "GameScreen", StringComparison.Ordinal)
                    && (status.MainPlayerReady
                        || !serverSide && status.NetworkState == "Playing")),
            timeout,
            $"Node '{node.Role}' did not reach player selection or a playable game.",
            cancellationToken);

    public static async Task EnterWorldWhenReadyAsync(
        GameNodeProcess node,
        string playerName,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            NodeStatus status = await node.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(status.Screen, "GameScreen", StringComparison.Ordinal)
                && status.MainPlayerReady) {
                return;
            }
            if (string.Equals(status.Screen, "PlayerScreen", StringComparison.Ordinal)) {
                try {
                    _ = await node.SendAsync<EnterWorldCommand, ControlAck>(
                        ControlCommandNames.EnterWorld,
                        new EnterWorldCommand(playerName),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (ControlCommandException exception) {
                    lastError = exception;
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' PlayerScreen did not become enterable. LastError={lastError?.Message}");
    }

    public static async Task EnsureClientPlayingAsync(
        GameNodeProcess client,
        string playerName,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        NodeStatus status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(status.Screen, "PlayerScreen", StringComparison.Ordinal)) {
            await EnterWorldWhenReadyAsync(
                client,
                playerName,
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        _ = await client.WaitForStatusAsync(
            value => value.ProjectLoaded
                && value.IsClientRunning
                && value.NetworkState == "Playing"
                && value.MainPlayerReady
                && string.Equals(value.Screen, "GameScreen", StringComparison.Ordinal),
            timeout,
            "Client did not reach NetworkState.Playing with a player entity.",
            cancellationToken).ConfigureAwait(false);
    }
}
