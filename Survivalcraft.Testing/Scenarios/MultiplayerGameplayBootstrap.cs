using System.Globalization;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Processes;

namespace Survivalcraft.Testing.Scenarios;

public static class MultiplayerGameplayBootstrap {
    public static async Task<int> StartCreativeClusterAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        RequiredAutomationCapabilities requiredCapabilities,
        IDictionary<string, AutomationDescription> capabilities,
        string worldName,
        int seed,
        int initialClientCount,
        TimeSpan stepTimeout,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);
        if (initialClientCount is < 1 || initialClientCount > cluster.Clients.Length) {
            throw new ArgumentOutOfRangeException(nameof(initialClientCount));
        }

        await runner.RunAsync(
            "start-cluster",
            "process.start(server, client-a, client-b, relay?)",
            token => cluster.StartAsync(token),
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "wait-main-menu",
            ControlCommandNames.GetStatus,
            token => Task.WhenAll(AllNodes(cluster).Select(node =>
                MultiplayerScenarioOperations.WaitForMainMenuAsync(
                    node,
                    stepTimeout,
                    token))),
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "require-v2-capabilities",
            ControlCommandNames.Describe,
            async token => {
                foreach (GameNodeProcess node in AllNodes(cluster)) {
                    capabilities[node.Role] = await node.RequireCapabilitiesAsync(
                        requiredCapabilities,
                        token).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "configure-determinism",
            ControlCommandNames.ConfigureNode,
            token => cluster.ConfigureDeterminismAsync(token),
            cancellationToken).ConfigureAwait(false);

        _ = await runner.RunAsync(
            "create-server-world",
            ControlCommandNames.CreateWorld,
            token => cluster.Server.SendAsync<CreateWorldCommand, CreateWorldResult>(
                ControlCommandNames.CreateWorld,
                new CreateWorldCommand(worldName, seed.ToString(CultureInfo.InvariantCulture)),
                timeout: stepTimeout,
                cancellationToken: token),
            result => result.DirectoryName,
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "enter-server-world",
            ControlCommandNames.EnterWorld,
            async token => {
                NodeStatus entry = await MultiplayerScenarioOperations.WaitForWorldEntryAsync(
                    cluster.Server,
                    serverSide: true,
                    stepTimeout,
                    token).ConfigureAwait(false);
                if (string.Equals(entry.Screen, "PlayerScreen", StringComparison.Ordinal)) {
                    await MultiplayerScenarioOperations.EnterWorldWhenReadyAsync(
                        cluster.Server,
                        "Host",
                        stepTimeout,
                        token).ConfigureAwait(false);
                }
                _ = await cluster.Server.WaitForStatusAsync(
                    status => status.ProjectLoaded
                        && status.IsServerRunning
                        && status.MainPlayerReady
                        && string.Equals(status.Screen, "GameScreen", StringComparison.Ordinal),
                    stepTimeout,
                    "Server did not finish entering its world.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        _ = await runner.RunAsync(
            "start-transport-host",
            "network.startHost",
            token => cluster.StartHostAsync(stepTimeout, token),
            room => room ?? "direct",
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "connect-initial-clients",
            $"network.connect({string.Join(", ", cluster.Clients.Take(initialClientCount).Select(client => client.Role))})",
            async token => {
                for (int index = 0; index < initialClientCount; index++) {
                    GameNodeProcess client = cluster.Clients[index];
                    await ConnectClientAndEnterAsync(
                        cluster,
                        client,
                        $"Learner{(char)('A' + index)}",
                        stepTimeout,
                        token).ConfigureAwait(false);
                }
                _ = await cluster.Server.WaitForStatusAsync(
                    status => status.ServerSessionCount == initialClientCount,
                    stepTimeout,
                    $"Server did not expose {initialClientCount} initial client sessions.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        NodeStatus actorStatus = await runner.RunAsync(
            "resolve-actor",
            ControlCommandNames.GetStatus,
            token => cluster.Clients[0].GetStatusAsync(token),
            status => $"playerIndex={status.PlayerIndex}",
            cancellationToken).ConfigureAwait(false);
        if (actorStatus.PlayerIndex < 0 || !actorStatus.MainPlayerReady) {
            throw new InvalidOperationException(
                $"Client A did not expose a ready main player. Status={actorStatus}.");
        }
        return actorStatus.PlayerIndex;
    }

    public static async Task ConnectClientAndEnterAsync(
        MultiplayerCluster cluster,
        GameNodeProcess client,
        string playerName,
        TimeSpan stepTimeout,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        await cluster.ConnectClientAsync(client, cancellationToken).ConfigureAwait(false);
        _ = await MultiplayerScenarioOperations.WaitForWorldEntryAsync(
            client,
            serverSide: false,
            stepTimeout,
            cancellationToken).ConfigureAwait(false);
        await MultiplayerScenarioOperations.EnsureClientPlayingAsync(
            client,
            playerName,
            stepTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    public static IEnumerable<GameNodeProcess> AllNodes(MultiplayerCluster cluster) =>
        new[] { cluster.Server }.Concat(cluster.Clients);
}
