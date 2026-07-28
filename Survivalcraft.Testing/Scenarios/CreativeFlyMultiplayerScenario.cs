using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record CreativeFlyMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    SetCreativeFlyCommand[]? ReplayActions = null);

public sealed record CreativeFlyMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    TimeSpan Elapsed);

public sealed class CreativeFlyMultiplayerScenarioException : Exception {
    public CreativeFlyMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class CreativeFlyMultiplayerScenario {
    public const string ScenarioName = "creative-fly-multiplayer";
    public const int ScenarioVersion = 1;

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-cluster", "process.start(server, client-a, client-b, relay?)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("require-v2-capabilities", ControlCommandNames.Describe),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("start-transport-host", "network.startHost"),
        new("connect-two-clients", "network.connect(client-a, client-b)"),
        new("reset-events", ControlCommandNames.ResetEvents),
        new("capture-baseline", ControlCommandNames.GetSnapshotV2),
        new("enable-creative-fly", ControlCommandNames.SetCreativeFly),
        new("verify-enabled-convergence", ControlCommandNames.GetSnapshotV2),
        new("verify-action-events", ControlCommandNames.GetEvents),
        new("disable-creative-fly", ControlCommandNames.SetCreativeFly),
        new("verify-disabled-convergence", ControlCommandNames.GetSnapshotV2)
    ];

    public async Task<CreativeFlyMultiplayerScenarioResult> RunAsync(
        CreativeFlyMultiplayerScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory, options.Transport);

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(6);
        if (stepTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.StepTimeout));
        }
        if (scenarioTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.ScenarioTimeout));
        }
        SetCreativeFlyCommand[] actionPlan = options.ReplayActions
            ?? [new SetCreativeFlyCommand(true), new SetCreativeFlyCommand(false)];
        if (actionPlan.Length != 2 || !actionPlan[0].Enabled || actionPlan[1].Enabled) {
            throw new ArgumentException(
                "Creative-fly scenario v1 requires recorded actions [enabled=true, enabled=false].",
                nameof(options.ReplayActions));
        }

        string runId = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string workBase = Path.GetFullPath(options.WorkDirectory
            ?? Path.Combine(Path.GetTempPath(), "Survivalcraft.TestHost"));
        string runDirectory = Path.Combine(workBase, $"{ScenarioName}-{runId}");
        Directory.CreateDirectory(runDirectory);

        ScenarioDefinition definition = new(
            ScenarioName,
            ScenarioVersion,
            options.Seed,
            ["server", "client-a", "client-b"],
            StepDefinitions);
        ScenarioRunner runner = new(stepTimeout);
        Dictionary<string, AutomationDescription> capabilities = new(StringComparer.Ordinal);
        Dictionary<string, StateSnapshotV2> snapshots = new(StringComparer.Ordinal);
        Dictionary<string, ActionEventsResult> events = new(StringComparer.Ordinal);
        List<ScenarioActionRecordV2> actions = [];
        List<ScenarioAssertionRecordV2> assertions = [];
        Dictionary<string, string> metrics = new(StringComparer.Ordinal);
        Dictionary<string, string[]> logs = new(StringComparer.Ordinal);
        Stopwatch stopwatch = Stopwatch.StartNew();
        MultiplayerCluster? cluster = null;
        Exception? scenarioFailure = null;
        Exception? artifactFailure = null;
        int actorPlayerIndex = -1;

        using CancellationTokenSource scenarioSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scenarioSource.CancelAfter(scenarioTimeout);
        CancellationToken scenarioToken = scenarioSource.Token;

        try {
            cluster = new MultiplayerCluster(new MultiplayerClusterOptions(
                runtimeDirectory,
                runDirectory,
                options.Transport,
                options.Seed,
                ClientCount: 2,
                StartupTimeout: stepTimeout));

            await runner.RunAsync(
                "start-cluster",
                "process.start(server, client-a, client-b, relay?)",
                token => cluster.StartAsync(token),
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "wait-main-menu",
                ControlCommandNames.GetStatus,
                token => Task.WhenAll(AllNodes(cluster).Select(node =>
                    MultiplayerScenarioOperations.WaitForMainMenuAsync(
                        node,
                        stepTimeout,
                        token))),
                scenarioToken).ConfigureAwait(false);

            RequiredAutomationCapabilities requiredCapabilities = new(
                SnapshotSchemaVersion: AutomationProtocol.CurrentSnapshotSchemaVersion,
                Commands: [
                    ControlCommandNames.Describe,
                    ControlCommandNames.GetSnapshotV2,
                    ControlCommandNames.GetEvents,
                    ControlCommandNames.ResetEvents,
                    ControlCommandNames.SetCreativeFly
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Player
                ]);
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
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "configure-determinism",
                ControlCommandNames.ConfigureNode,
                token => cluster.ConfigureDeterminismAsync(token),
                scenarioToken).ConfigureAwait(false);

            _ = await runner.RunAsync(
                "create-server-world",
                ControlCommandNames.CreateWorld,
                token => cluster.Server.SendAsync<CreateWorldCommand, CreateWorldResult>(
                    ControlCommandNames.CreateWorld,
                    new CreateWorldCommand(
                        $"CreativeFly-{options.Transport}-{options.Seed}",
                        options.Seed.ToString(CultureInfo.InvariantCulture)),
                    timeout: stepTimeout,
                    cancellationToken: token),
                result => result.DirectoryName,
                scenarioToken).ConfigureAwait(false);

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
                scenarioToken).ConfigureAwait(false);

            _ = await runner.RunAsync(
                "start-transport-host",
                "network.startHost",
                token => cluster.StartHostAsync(stepTimeout, token),
                room => room ?? "direct",
                scenarioToken).ConfigureAwait(false);

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
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "reset-events",
                ControlCommandNames.ResetEvents,
                token => Task.WhenAll(AllNodes(cluster).Select(node =>
                    node.SendAsync<EmptyCommand, ResetEventsResult>(
                        ControlCommandNames.ResetEvents,
                        new EmptyCommand(),
                        cancellationToken: token))),
                scenarioToken).ConfigureAwait(false);

            Dictionary<string, StateSnapshotV2> baseline = await runner.RunAsync(
                "capture-baseline",
                ControlCommandNames.GetSnapshotV2,
                token => CaptureAllAsync(cluster, token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            foreach ((string role, StateSnapshotV2 snapshot) in baseline) {
                snapshots[$"{role}-baseline"] = snapshot;
            }
            PlayerSnapshotV2 actor = baseline[cluster.Clients[0].Role].Players
                .Single(player => player.IsMainPlayer && player.ReadyForPlaying);
            actorPlayerIndex = actor.PlayerIndex;

            SetCreativeFlyCommand enableAction = actionPlan[0] with {
                PlayerIndex = actorPlayerIndex
            };
            ActionReceipt enabledReceipt = await runner.RunAsync(
                "enable-creative-fly",
                ControlCommandNames.SetCreativeFly,
                token => cluster.Clients[0].SendAsync<SetCreativeFlyCommand, ActionReceipt>(
                    ControlCommandNames.SetCreativeFly,
                    enableAction,
                    cancellationToken: token),
                receipt => $"actor={receipt.ActorPlayerIndex}; code={receipt.Code}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(enabledReceipt);
            actions.Add(new ScenarioActionRecordV2(
                cluster.Clients[0].Role,
                ControlCommandNames.SetCreativeFly,
                JsonSerializer.SerializeToElement(enableAction, ControlProtocol.JsonOptions),
                enabledReceipt));

            Dictionary<string, StateSnapshotV2> enabled = await runner.RunAsync(
                "verify-enabled-convergence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForCreativeFlyOnAllAsync(
                    cluster,
                    actorPlayerIndex,
                    expected: true,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            foreach ((string role, StateSnapshotV2 snapshot) in enabled) {
                snapshots[$"{role}-enabled"] = snapshot;
            }
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-enabled-convergence",
                "passed",
                AllNodes(cluster).Select(node => node.Role).ToArray(),
                $"playerIndex={actorPlayerIndex}; enabled=true"));

            ActionEventsResult enabledEvents = await runner.RunAsync(
                "verify-action-events",
                ControlCommandNames.GetEvents,
                token => cluster.Clients[0].SendAsync<GetEventsCommand, ActionEventsResult>(
                    ControlCommandNames.GetEvents,
                    new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureActionEvents(enabledEvents, enabledReceipt.ActionId);
            events[cluster.Clients[0].Role] = enabledEvents;
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-action-events",
                "passed",
                [cluster.Clients[0].Role],
                $"actionId={enabledReceipt.ActionId}; phases=queued,applied"));

            SetCreativeFlyCommand disableAction = actionPlan[1] with {
                PlayerIndex = actorPlayerIndex
            };
            ActionReceipt disabledReceipt = await runner.RunAsync(
                "disable-creative-fly",
                ControlCommandNames.SetCreativeFly,
                token => cluster.Clients[0].SendAsync<SetCreativeFlyCommand, ActionReceipt>(
                    ControlCommandNames.SetCreativeFly,
                    disableAction,
                    cancellationToken: token),
                receipt => $"actor={receipt.ActorPlayerIndex}; code={receipt.Code}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(disabledReceipt);
            actions.Add(new ScenarioActionRecordV2(
                cluster.Clients[0].Role,
                ControlCommandNames.SetCreativeFly,
                JsonSerializer.SerializeToElement(disableAction, ControlProtocol.JsonOptions),
                disabledReceipt));

            Dictionary<string, StateSnapshotV2> disabled = await runner.RunAsync(
                "verify-disabled-convergence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForCreativeFlyOnAllAsync(
                    cluster,
                    actorPlayerIndex,
                    expected: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            foreach ((string role, StateSnapshotV2 snapshot) in disabled) {
                snapshots[$"{role}-disabled"] = snapshot;
            }
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-disabled-convergence",
                "passed",
                AllNodes(cluster).Select(node => node.Role).ToArray(),
                $"playerIndex={actorPlayerIndex}; enabled=false"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(cluster, snapshots, events).ConfigureAwait(false);
                foreach (GameNodeProcess node in AllNodes(cluster)) {
                    logs[node.Role] = node.Logs.ToArray();
                }
                if (cluster.Relay != null) {
                    logs["relay"] = cluster.Relay.Logs.ToArray();
                    metrics["relay.port"] = cluster.Relay.Port.ToString(CultureInfo.InvariantCulture);
                }
                await cluster.DisposeAsync().ConfigureAwait(false);
            }

            metrics["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
            metrics["actorPlayerIndex"] = actorPlayerIndex.ToString(CultureInfo.InvariantCulture);
            metrics["transport"] = options.Transport.ToString();
            try {
                ScenarioReplayV2 replay = new(
                    ScenarioName,
                    ScenarioVersion,
                    options.Seed,
                    runtimeDirectory,
                    options.Transport.ToString(),
                    ControlProtocol.CurrentVersion,
                    AutomationProtocol.CurrentApiVersion,
                    AutomationProtocol.CurrentSnapshotSchemaVersion);
                ScenarioTopologyV2 topology = new(
                    options.Transport.ToString(),
                    "server",
                    ["client-a", "client-b"],
                    options.Transport == MultiplayerTransportKind.Relay,
                    cluster?.RoomCode);
                await ScenarioArtifactWriter.WriteV2Async(
                    artifactDirectory,
                    new ScenarioArtifactBundleV2(
                        definition,
                        replay,
                        topology,
                        runner.Steps.ToArray(),
                        capabilities,
                        actions.ToArray(),
                        events,
                        snapshots,
                        assertions.ToArray(),
                        logs,
                        metrics,
                        scenarioFailure == null ? "passed" : "failed",
                        scenarioFailure?.ToString()),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) {
                artifactFailure = exception;
            }

            if (!options.KeepData && !IsPathInside(artifactDirectory, runDirectory)) {
                TryDeleteCreatedRunDirectory(runDirectory, workBase);
            }
        }

        if (scenarioFailure != null) {
            throw new CreativeFlyMultiplayerScenarioException(
                $"Creative-fly multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new CreativeFlyMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new CreativeFlyMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            stopwatch.Elapsed);
    }

    private static IEnumerable<GameNodeProcess> AllNodes(MultiplayerCluster cluster) =>
        new[] { cluster.Server }.Concat(cluster.Clients);

    private static async Task<Dictionary<string, StateSnapshotV2>> CaptureAllAsync(
        MultiplayerCluster cluster,
        CancellationToken cancellationToken) {
        Task<StateSnapshotV2>[] captureTasks = AllNodes(cluster)
            .Select(node => CaptureAsync(node, cancellationToken))
            .ToArray();
        StateSnapshotV2[] captured = await Task.WhenAll(captureTasks).ConfigureAwait(false);
        return AllNodes(cluster)
            .Select((node, index) => (node.Role, Snapshot: captured[index]))
            .ToDictionary(item => item.Role, item => item.Snapshot, StringComparer.Ordinal);
    }

    private static Task<StateSnapshotV2> CaptureAsync(
        GameNodeProcess node,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command([
                SnapshotDomain.Network,
                SnapshotDomain.Players
            ]),
            cancellationToken: cancellationToken);

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForCreativeFlyOnAllAsync(
        MultiplayerCluster cluster,
        int playerIndex,
        bool expected,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        Task<StateSnapshotV2>[] waits = AllNodes(cluster)
            .Select(node => WaitForCreativeFlyAsync(
                node,
                playerIndex,
                expected,
                timeout,
                cancellationToken))
            .ToArray();
        StateSnapshotV2[] captured = await Task.WhenAll(waits).ConfigureAwait(false);
        return AllNodes(cluster)
            .Select((node, index) => (node.Role, Snapshot: captured[index]))
            .ToDictionary(item => item.Role, item => item.Snapshot, StringComparer.Ordinal);
    }

    private static async Task<StateSnapshotV2> WaitForCreativeFlyAsync(
        GameNodeProcess node,
        int playerIndex,
        bool expected,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                ControlCommandNames.GetSnapshotV2,
                new GetSnapshotV2Command(
                    [SnapshotDomain.Network, SnapshotDomain.Players],
                    PlayerIndexes: [playerIndex]),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            PlayerSnapshotV2? player = last.Players.SingleOrDefault(item =>
                item.PlayerIndex == playerIndex);
            if (player?.ReadyForPlaying == true
                && player.CreativeFlyEnabled == expected) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        string playerText = last?.Players.SingleOrDefault()?.ToString() ?? "missing";
        throw new TimeoutException(
            $"Node '{node.Role}' player {playerIndex} did not converge to creativeFly={expected}. Last={playerText}.");
    }

    private static void EnsureAccepted(ActionReceipt receipt) {
        if (!receipt.Accepted) {
            throw new InvalidOperationException(
                $"Action '{receipt.ActionId}' was rejected with code '{receipt.Code}': {receipt.Detail}");
        }
    }

    private static void EnsureActionEvents(ActionEventsResult events, string actionId) {
        ActionEventPhase[] phases = events.Events
            .Where(item => string.Equals(item.ActionId, actionId, StringComparison.Ordinal))
            .Select(item => item.Phase)
            .ToArray();
        if (!phases.Contains(ActionEventPhase.Queued)
            || !phases.Contains(ActionEventPhase.Applied)) {
            throw new InvalidOperationException(
                $"Action '{actionId}' did not publish queued and applied events. Phases={string.Join(',', phases)}.");
        }
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            try {
                snapshots[$"{node.Role}-final"] = await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                    ControlCommandNames.GetSnapshotV2,
                    new GetSnapshotV2Command([
                        SnapshotDomain.Network,
                        SnapshotDomain.Players,
                        SnapshotDomain.Events
                    ], EventLimit: AutomationProtocol.MaximumEventQueryCount),
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Best-effort evidence collection must not replace the scenario failure.
            }
            try {
                events[node.Role] = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                    ControlCommandNames.GetEvents,
                    new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // A dead or wedged node can still contribute its retained process logs.
            }
        }
    }

    private static void ValidateRuntime(
        string runtimeDirectory,
        MultiplayerTransportKind transport) {
        if (!Directory.Exists(runtimeDirectory)) {
            throw new DirectoryNotFoundException(
                $"Game runtime directory '{runtimeDirectory}' does not exist.");
        }
        List<string> required = [
            "Survivalcraft.exe",
            "Survivalcraft.dll",
            "Survivalcraft.Networking.dll",
            Path.Combine("Mods", "Survivalcraft.TestAutomation.scmod")
        ];
        if (transport == MultiplayerTransportKind.Relay) {
            required.Add("Survivalcraft.RelayServer.dll");
        }
        string[] missing = required
            .Where(path => !File.Exists(Path.Combine(runtimeDirectory, path)))
            .ToArray();
        if (missing.Length > 0) {
            throw new InvalidDataException(
                $"Runtime '{runtimeDirectory}' is incomplete. Missing: {string.Join(", ", missing)}");
        }
    }

    private static bool IsPathInside(string candidatePath, string parentPath) {
        string candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(
                parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteCreatedRunDirectory(
        string runDirectory,
        string workBase) {
        try {
            string fullRunDirectory = Path.GetFullPath(runDirectory);
            string fullWorkBase = Path.GetFullPath(workBase);
            if (!IsPathInside(fullRunDirectory, fullWorkBase)
                || string.Equals(
                    fullRunDirectory.TrimEnd(Path.DirectorySeparatorChar),
                    fullWorkBase.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)) {
                return;
            }
            if (Directory.Exists(fullRunDirectory)) {
                Directory.Delete(fullRunDirectory, recursive: true);
            }
        }
        catch {
            // Preserving an isolated run directory is safer than masking scenario results.
        }
    }
}
