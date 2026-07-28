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
        TimeSpan stepTimeout,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);

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
            "connect-two-clients",
            "network.connect(client-a, client-b)",
            async token => {
                for (int index = 0; index < cluster.Clients.Length; index++) {
                    GameNodeProcess client = cluster.Clients[index];
                    await cluster.ConnectClientAsync(client, token).ConfigureAwait(false);
                    _ = await MultiplayerScenarioOperations.WaitForWorldEntryAsync(
                        client,
                        serverSide: false,
                        stepTimeout,
                        token).ConfigureAwait(false);
                    await MultiplayerScenarioOperations.EnsureClientPlayingAsync(
                        client,
                        $"Learner{(char)('A' + index)}",
                        stepTimeout,
                        token).ConfigureAwait(false);
                }
                _ = await cluster.Server.WaitForStatusAsync(
                    status => status.ServerSessionCount == cluster.Clients.Length,
                    stepTimeout,
                    "Server did not expose both client sessions.",
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

    public static IEnumerable<GameNodeProcess> AllNodes(MultiplayerCluster cluster) =>
        new[] { cluster.Server }.Concat(cluster.Clients);
}
