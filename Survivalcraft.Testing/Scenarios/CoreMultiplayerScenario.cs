using System.Diagnostics;
using System.Globalization;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record CoreMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed = 20260728,
    NetworkFaultProfile? NetworkFaults = null,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false);

public sealed record CoreMultiplayerScenarioResult(
    string ArtifactDirectory,
    string WorldDirectoryName,
    TerrainCellState VerifiedCell,
    SnapshotDiff PersistenceDiff,
    TimeSpan Elapsed);

public sealed class CoreMultiplayerScenarioException : Exception {
    public CoreMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class CoreMultiplayerScenario {
    public const string ScenarioName = "core-multiplayer";
    public const int ScenarioVersion = 2;

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-nodes", "process.start(server, client)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("connect-client", ControlCommandNames.ConnectClient),
        new("enter-client-world", ControlCommandNames.EnterWorld),
        new("exchange-mod-channel-messages", ControlCommandNames.SendTestModMessage),
        new("isolate-mod-channel-handler-failure", ControlCommandNames.SendTestModMessage),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("mutate-and-sync-terrain", ControlCommandNames.ChangeTerrainCell),
        new("disconnect-client", ControlCommandNames.Disconnect),
        new("reconnect-client", ControlCommandNames.ConnectClient),
        new("verify-reconnected-state", ControlCommandNames.GetSnapshot),
        new("verify-mod-channel-after-reconnect", ControlCommandNames.SendTestModMessage),
        new("save-and-stop-world", ControlCommandNames.SaveWorld),
        new("reload-saved-world", ControlCommandNames.ReloadWorld),
        new("verify-mod-channel-after-world-reload", ControlCommandNames.SendTestModMessage),
        new("verify-persistence", ControlCommandNames.GetSnapshot)
    ];

    public async Task<CoreMultiplayerScenarioResult> RunAsync(
        CoreMultiplayerScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory);

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        if (stepTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.StepTimeout));
        }
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(6);
        if (scenarioTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.ScenarioTimeout));
        }

        NetworkFaultProfile faultProfile = (options.NetworkFaults
            ?? new NetworkFaultProfile(
                Seed: options.Seed,
                BaseLatencyMilliseconds: 12,
                JitterMilliseconds: 3)).Validate();
        string runId = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string workBase = Path.GetFullPath(options.WorkDirectory
            ?? Path.Combine(Path.GetTempPath(), "Survivalcraft.TestHost"));
        string runDirectory = Path.Combine(workBase, $"{ScenarioName}-{runId}");
        Directory.CreateDirectory(runDirectory);

        ScenarioDefinition definition = new(
            ScenarioName,
            ScenarioVersion,
            options.Seed,
            ["server", "client"],
            StepDefinitions);
        ScenarioReplay replay = new(
            ScenarioName,
            ScenarioVersion,
            options.Seed,
            runtimeDirectory,
            faultProfile);
        List<ScenarioStepRecord> steps = [];
        Dictionary<string, StateSnapshot> snapshots = new(StringComparer.Ordinal);
        Dictionary<string, string> metrics = new(StringComparer.Ordinal);
        Stopwatch stopwatch = Stopwatch.StartNew();
        GameNodeProcess? server = null;
        GameNodeProcess? client = null;
        CreateWorldResult? createdWorld = null;
        TerrainMutationResult? terrainMutation = null;
        SnapshotDiff? persistenceDiff = null;
        Exception? scenarioFailure = null;
        Exception? artifactFailure = null;

        using CancellationTokenSource scenarioSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scenarioSource.CancelAfter(scenarioTimeout);
        CancellationToken scenarioToken = scenarioSource.Token;

        async Task<T> StepAsync<T>(
            string name,
            string command,
            Func<Task<T>> action,
            Func<T, string?>? summarize = null) {
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            try {
                T result = await action().ConfigureAwait(false);
                steps.Add(new ScenarioStepRecord(
                    name,
                    "passed",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    summarize?.Invoke(result)));
                return result;
            }
            catch (Exception exception) {
                steps.Add(new ScenarioStepRecord(
                    name,
                    "failed",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    command,
                    exception.ToString()));
                throw;
            }
        }

        async Task StepActionAsync(string name, string command, Func<Task> action) {
            await StepAsync(
                name,
                command,
                async () => {
                    await action().ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
        }

        try {
            server = new GameNodeProcess(new GameNodeOptions(
                "server",
                runtimeDirectory,
                Path.Combine(runDirectory, "server"),
                StartupTimeout: stepTimeout));
            client = new GameNodeProcess(new GameNodeOptions(
                "client",
                runtimeDirectory,
                Path.Combine(runDirectory, "client"),
                StartupTimeout: stepTimeout));

            await StepActionAsync(
                "start-nodes",
                "process.start(server, client)",
                () => Task.WhenAll(
                    server.StartAsync(scenarioToken),
                    client.StartAsync(scenarioToken))).ConfigureAwait(false);

            await StepActionAsync(
                "wait-main-menu",
                ControlCommandNames.GetStatus,
                () => Task.WhenAll(
                    WaitForMainMenuAsync(server, stepTimeout, scenarioToken),
                    WaitForMainMenuAsync(client, stepTimeout, scenarioToken))).ConfigureAwait(false);

            await StepActionAsync(
                "configure-determinism",
                ControlCommandNames.ConfigureNode,
                async () => {
                    _ = await server.SendAsync<ConfigureNodeCommand, ControlAck>(
                        ControlCommandNames.ConfigureNode,
                        new ConfigureNodeCommand("server", options.Seed),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await client.SendAsync<ConfigureNodeCommand, ControlAck>(
                        ControlCommandNames.ConfigureNode,
                        new ConfigureNodeCommand("client", options.Seed),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            createdWorld = await StepAsync(
                "create-server-world",
                ControlCommandNames.CreateWorld,
                () => server.SendAsync<CreateWorldCommand, CreateWorldResult>(
                    ControlCommandNames.CreateWorld,
                    new CreateWorldCommand(
                        $"Automated-{options.Seed}",
                        options.Seed.ToString(CultureInfo.InvariantCulture)),
                    timeout: stepTimeout,
                    cancellationToken: scenarioToken),
                result => result.DirectoryName).ConfigureAwait(false);

            await StepActionAsync(
                "enter-server-world",
                ControlCommandNames.EnterWorld,
                async () => {
                    NodeStatus entry = await WaitForWorldEntryAsync(
                        server,
                        serverSide: true,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    if (IsScreen(entry, "PlayerScreen")) {
                        await EnterWorldWhenReadyAsync(
                            server,
                            "Host",
                            stepTimeout,
                            scenarioToken).ConfigureAwait(false);
                    }
                    _ = await server.WaitForStatusAsync(
                        status => status.ProjectLoaded
                            && status.IsServerRunning
                            && status.MainPlayerReady
                            && IsScreen(status, "GameScreen"),
                        stepTimeout,
                        "Server did not finish entering its world.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "connect-client",
                ControlCommandNames.ConnectClient,
                async () => {
                    _ = await client.SendAsync<ConnectClientCommand, ControlAck>(
                        ControlCommandNames.ConnectClient,
                        new ConnectClientCommand("127.0.0.1", server.GamePort),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForWorldEntryAsync(
                        client,
                        serverSide: false,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "enter-client-world",
                ControlCommandNames.EnterWorld,
                () => EnsureClientPlayingAsync(
                    client,
                    "Learner",
                    stepTimeout,
                    scenarioToken)).ConfigureAwait(false);

            await StepActionAsync(
                "exchange-mod-channel-messages",
                ControlCommandNames.SendTestModMessage,
                async () => {
                    _ = await server.SendAsync<EmptyCommand, TestModMessagesStatus>(
                        ControlCommandNames.ResetTestModMessages,
                        new EmptyCommand(),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await client.SendAsync<EmptyCommand, TestModMessagesStatus>(
                        ControlCommandNames.ResetTestModMessages,
                        new EmptyCommand(),
                        cancellationToken: scenarioToken).ConfigureAwait(false);

                    NetworkFaultProfile duplicateEveryPacket = new(
                        Seed: unchecked(options.Seed + 101),
                        DuplicationRate: 1);
                    _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(duplicateEveryPacket),
                        cancellationToken: scenarioToken).ConfigureAwait(false);

                    string duplicateCorrelation = $"duplicate-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "deduplicated-client-message",
                            CorrelationId: duplicateCorrelation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    TestModMessagesStatus serverMessages = await WaitForTestModStateAsync(
                        server,
                        status => CountTestMessages(status, duplicateCorrelation) == 1
                            && status.Channel.Metrics.Duplicates >= 1,
                        stepTimeout,
                        $"Server did not deduplicate '{duplicateCorrelation}'.",
                        scenarioToken).ConfigureAwait(false);
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, duplicateCorrelation) == 1,
                        stepTimeout,
                        $"Client did not receive echo '{duplicateCorrelation}'.",
                        scenarioToken).ConfigureAwait(false);
                    metrics["modChannel.duplicates"] = serverMessages.Channel.Metrics.Duplicates
                        .ToString(CultureInfo.InvariantCulture);

                    _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(new NetworkFaultProfile(Seed: options.Seed + 102)),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    string broadcastCorrelation = $"broadcast-{options.Seed}";
                    TestModSendResult broadcast =
                        await server.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                            ControlCommandNames.SendTestModMessage,
                            new SendTestModMessageCommand(
                                "server-broadcast",
                                Broadcast: true,
                                CorrelationId: broadcastCorrelation),
                            cancellationToken: scenarioToken).ConfigureAwait(false);
                    if (broadcast.RecipientCount != 1) {
                        throw new InvalidOperationException(
                            $"Expected one broadcast recipient, got {broadcast.RecipientCount}.");
                    }
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, broadcastCorrelation) == 1,
                        stepTimeout,
                        $"Client did not receive broadcast '{broadcastCorrelation}'.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "isolate-mod-channel-handler-failure",
                ControlCommandNames.SendTestModMessage,
                async () => {
                    string failedCorrelation = $"handler-failure-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "intentional-isolated-failure",
                            FailHandler: true,
                            CorrelationId: failedCorrelation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    TestModMessagesStatus failed = await WaitForTestModStateAsync(
                        server,
                        status => CountTestMessages(status, failedCorrelation) == 1
                            && status.Channel.Metrics.HandlerErrors >= 1,
                        stepTimeout,
                        "Server did not isolate the requested mod handler failure.",
                        scenarioToken).ConfigureAwait(false);
                    metrics["modChannel.handlerErrors"] = failed.Channel.Metrics.HandlerErrors
                        .ToString(CultureInfo.InvariantCulture);

                    string recoveryCorrelation = $"handler-recovery-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "handler-still-operational",
                            CorrelationId: recoveryCorrelation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, recoveryCorrelation) == 1,
                        stepTimeout,
                        "Mod channel did not recover after an isolated handler failure.",
                        scenarioToken).ConfigureAwait(false);
                    NodeStatus clientStatus = await client.GetStatusAsync(scenarioToken)
                        .ConfigureAwait(false);
                    if (!clientStatus.IsClientRunning || !clientStatus.ProjectLoaded) {
                        throw new InvalidOperationException(
                            "The isolated mod handler failure terminated the multiplayer session.");
                    }
                }).ConfigureAwait(false);

            await StepActionAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                async () => {
                    _ = await server.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(faultProfile),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    NetworkFaultProfile clientProfile = faultProfile with {
                        Seed = unchecked(faultProfile.Seed + 1)
                    };
                    _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(clientProfile),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            terrainMutation = await StepAsync(
                "mutate-and-sync-terrain",
                ControlCommandNames.ChangeTerrainCell,
                async () => {
                    TerrainMutationResult mutation =
                        await server.SendAsync<ChangeTerrainCellCommand, TerrainMutationResult>(
                            ControlCommandNames.ChangeTerrainCell,
                            new ChangeTerrainCellCommand(Block: TestBlockKind.Glass),
                            cancellationToken: scenarioToken).ConfigureAwait(false);
                    TerrainCellCoordinate coordinate = ToCoordinate(mutation.Cell);
                    StateSnapshot serverSnapshot = await CaptureAsync(
                        server,
                        coordinate,
                        scenarioToken).ConfigureAwait(false);
                    StateSnapshot clientSnapshot = await WaitForCellAsync(
                        client,
                        coordinate,
                        mutation.Cell.Value,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    snapshots["server-after-mutation"] = serverSnapshot;
                    snapshots["client-after-sync"] = clientSnapshot;
                    return mutation;
                },
                mutation => FormatCell(mutation.Cell)).ConfigureAwait(false);

            await StepActionAsync(
                "disconnect-client",
                ControlCommandNames.Disconnect,
                async () => {
                    _ = await client.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await client.WaitForStatusAsync(
                        status => !status.ProjectLoaded
                            && !status.IsClientRunning
                            && IsScreen(status, "MainMenuScreen"),
                        stepTimeout,
                        "Client did not return to MainMenu after disconnect.",
                        scenarioToken).ConfigureAwait(false);
                    _ = await server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 0,
                        stepTimeout,
                        "Server retained the disconnected client session.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "reconnect-client",
                ControlCommandNames.ConnectClient,
                async () => {
                    _ = await client.SendAsync<ConnectClientCommand, ControlAck>(
                        ControlCommandNames.ConnectClient,
                        new ConnectClientCommand("127.0.0.1", server.GamePort),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForWorldEntryAsync(
                        client,
                        serverSide: false,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    await EnsureClientPlayingAsync(
                        client,
                        "Learner",
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "verify-reconnected-state",
                ControlCommandNames.GetSnapshot,
                async () => {
                    TerrainCellCoordinate coordinate = ToCoordinate(terrainMutation.Cell);
                    snapshots["client-after-reconnect"] = await WaitForCellAsync(
                        client,
                        coordinate,
                        terrainMutation.Cell.Value,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "verify-mod-channel-after-reconnect",
                ControlCommandNames.SendTestModMessage,
                async () => {
                    string correlation = $"reconnect-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "channel-survived-reconnect",
                            CorrelationId: correlation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, correlation) == 1,
                        stepTimeout,
                        "Mod channel did not deliver after reconnect.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "save-and-stop-world",
                ControlCommandNames.SaveWorld,
                async () => {
                    SaveWorldResult saved = await server.SendAsync<SaveWorldCommand, SaveWorldResult>(
                        ControlCommandNames.SaveWorld,
                        new SaveWorldCommand(),
                        timeout: stepTimeout,
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    if (!saved.Saved) {
                        throw new InvalidOperationException("Server declined to save the storage-backed world.");
                    }
                    _ = await client.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 0,
                        stepTimeout,
                        "Server retained a client session before shutdown.",
                        scenarioToken).ConfigureAwait(false);
                    _ = await server.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(SaveWorld: true),
                        timeout: stepTimeout,
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await server.WaitForStatusAsync(
                        status => !status.ProjectLoaded
                            && !status.IsServerRunning
                            && IsScreen(status, "MainMenuScreen"),
                        stepTimeout,
                        "Server did not return to MainMenu after saving.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "reload-saved-world",
                ControlCommandNames.ReloadWorld,
                async () => {
                    _ = await server.SendAsync<ReloadWorldCommand, ControlAck>(
                        ControlCommandNames.ReloadWorld,
                        new ReloadWorldCommand(createdWorld.DirectoryName),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    NodeStatus entry = await WaitForWorldEntryAsync(
                        server,
                        serverSide: true,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    if (IsScreen(entry, "PlayerScreen")) {
                        await EnterWorldWhenReadyAsync(
                            server,
                            "Host",
                            stepTimeout,
                            scenarioToken).ConfigureAwait(false);
                    }
                    _ = await server.WaitForStatusAsync(
                        status => status.ProjectLoaded
                            && status.IsServerRunning
                            && status.MainPlayerReady
                            && IsScreen(status, "GameScreen"),
                        stepTimeout,
                        "Saved world did not become playable after reload.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "verify-mod-channel-after-world-reload",
                ControlCommandNames.SendTestModMessage,
                async () => {
                    _ = await client.SendAsync<ConnectClientCommand, ControlAck>(
                        ControlCommandNames.ConnectClient,
                        new ConnectClientCommand("127.0.0.1", server.GamePort),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForWorldEntryAsync(
                        client,
                        serverSide: false,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    await EnsureClientPlayingAsync(
                        client,
                        "Learner",
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);

                    string correlation = $"world-reload-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "channel-survived-world-reload",
                            CorrelationId: correlation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    TestModMessagesStatus delivered = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, correlation) == 1,
                        stepTimeout,
                        "Mod channel did not deliver after world reload.",
                        scenarioToken).ConfigureAwait(false);
                    if (delivered.Channel.OverrideActive
                        || delivered.Channel.Channels.Length == 0) {
                        throw new InvalidOperationException(
                            "The registered mod channel catalog was not restored after world reload.");
                    }
                    _ = await client.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 0,
                        stepTimeout,
                        "Server retained the world-reload verification session.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            persistenceDiff = await StepAsync(
                "verify-persistence",
                ControlCommandNames.GetSnapshot,
                async () => {
                    TerrainCellCoordinate coordinate = ToCoordinate(terrainMutation.Cell);
                    StateSnapshot reloaded = await WaitForCellAsync(
                        server,
                        coordinate,
                        terrainMutation.Cell.Value,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    snapshots["server-after-reload"] = reloaded;
                    SnapshotDiff diff = SnapshotComparer.Compare(
                        ToTerrainOnlySnapshot(snapshots["server-after-mutation"]),
                        ToTerrainOnlySnapshot(reloaded));
                    if (!diff.Equal) {
                        throw new InvalidOperationException(
                            "The terrain state changed across save/reload: "
                            + string.Join(", ", diff.Differences.Select(item => item.Path)));
                    }
                    return diff;
                },
                diff => diff.ActualHash).ConfigureAwait(false);
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            metrics["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
            metrics["seed"] = options.Seed.ToString(CultureInfo.InvariantCulture);
            metrics["stepCount"] = steps.Count.ToString(CultureInfo.InvariantCulture);
            metrics["passedStepCount"] = steps.Count(step => step.Status == "passed")
                .ToString(CultureInfo.InvariantCulture);
            if (terrainMutation != null) {
                metrics["verifiedCell"] = FormatCell(terrainMutation.Cell);
            }

            Dictionary<string, string[]> logs = new(StringComparer.Ordinal);
            if (server != null) {
                logs["server"] = server.Logs.ToArray();
            }
            if (client != null) {
                logs["client"] = client.Logs.ToArray();
            }
            ScenarioArtifactBundle bundle = new(
                definition,
                replay,
                steps.ToArray(),
                snapshots,
                persistenceDiff,
                logs,
                metrics,
                scenarioFailure == null ? "passed" : "failed",
                scenarioFailure?.ToString());
            try {
                _ = await ScenarioArtifactWriter.WriteAsync(
                    artifactDirectory,
                    bundle,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) {
                artifactFailure = exception;
            }

            if (client != null) {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            if (server != null) {
                await server.DisposeAsync().ConfigureAwait(false);
            }
            if (!options.KeepData && !IsPathInside(artifactDirectory, runDirectory)) {
                TryDeleteCreatedRunDirectory(runDirectory, workBase);
            }
        }

        if (scenarioFailure != null) {
            if (artifactFailure != null) {
                scenarioFailure = new AggregateException(scenarioFailure, artifactFailure);
            }
            throw new CoreMultiplayerScenarioException(
                $"Core multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new IOException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactFailure);
        }
        if (createdWorld == null || terrainMutation == null || persistenceDiff == null) {
            throw new InvalidOperationException("Scenario completed without producing its required result state.");
        }

        return new CoreMultiplayerScenarioResult(
            artifactDirectory,
            createdWorld.DirectoryName,
            terrainMutation.Cell,
            persistenceDiff,
            stopwatch.Elapsed);
    }

    private static async Task<TestModMessagesStatus> WaitForTestModStateAsync(
        GameNodeProcess node,
        Func<TestModMessagesStatus, bool> predicate,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(predicate);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        TestModMessagesStatus? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await node.SendAsync<EmptyCommand, TestModMessagesStatus>(
                ControlCommandNames.GetTestModMessages,
                new EmptyCommand(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (predicate(last)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"{failureMessage} Node={node.Role}; Last={last}.{Environment.NewLine}"
            + node.GetRecentLogText());
    }

    private static int CountTestMessages(
        TestModMessagesStatus status,
        string correlationId) =>
        status.Messages.Count(message => string.Equals(
            message.CorrelationId,
            correlationId,
            StringComparison.Ordinal));

    private static async Task<NodeStatus> WaitForMainMenuAsync(
        GameNodeProcess node,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await node.WaitForStatusAsync(
            status => status.ContentLoaded && IsScreen(status, "MainMenuScreen"),
            timeout,
            "Game content did not reach MainMenu.",
            cancellationToken).ConfigureAwait(false);

    private static async Task<NodeStatus> WaitForWorldEntryAsync(
        GameNodeProcess node,
        bool serverSide,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await node.WaitForStatusAsync(
            status => IsScreen(status, "PlayerScreen")
                || (status.ProjectLoaded
                    && IsScreen(status, "GameScreen")
                    && (status.MainPlayerReady || !serverSide && status.NetworkState == "Playing")),
            timeout,
            $"Node '{node.Role}' did not reach player selection or a playable game.",
            cancellationToken).ConfigureAwait(false);

    private static async Task EnsureClientPlayingAsync(
        GameNodeProcess client,
        string playerName,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        NodeStatus status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (IsScreen(status, "PlayerScreen")) {
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
                && IsScreen(value, "GameScreen"),
            timeout,
            "Client did not reach NetworkState.Playing with a player entity.",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnterWorldWhenReadyAsync(
        GameNodeProcess node,
        string playerName,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            NodeStatus status = await node.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (IsScreen(status, "GameScreen") && status.MainPlayerReady) {
                return;
            }
            if (IsScreen(status, "PlayerScreen")) {
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
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' PlayerScreen did not become enterable. LastError={lastError?.Message}");
    }

    private static async Task<StateSnapshot> WaitForCellAsync(
        GameNodeProcess node,
        TerrainCellCoordinate coordinate,
        int expectedValue,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshot? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureAsync(node, coordinate, cancellationToken).ConfigureAwait(false);
            if (last.TerrainCells is [TerrainCellState cell]
                && cell.Value == expectedValue) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        string actual = last?.TerrainCells.FirstOrDefault()?.Value.ToString(CultureInfo.InvariantCulture)
            ?? "missing";
        throw new TimeoutException(
            $"Node '{node.Role}' cell ({coordinate.X},{coordinate.Y},{coordinate.Z}) "
            + $"did not become {expectedValue}; last={actual}.");
    }

    private static Task<StateSnapshot> CaptureAsync(
        GameNodeProcess node,
        TerrainCellCoordinate coordinate,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotCommand, StateSnapshot>(
            ControlCommandNames.GetSnapshot,
            new GetSnapshotCommand([coordinate], IncludeEntities: false),
            cancellationToken: cancellationToken);

    private static StateSnapshot ToTerrainOnlySnapshot(StateSnapshot snapshot) =>
        new(
            snapshot.SchemaVersion,
            "terrain",
            0,
            0,
            string.Empty,
            string.Empty,
            null,
            [],
            [],
            snapshot.TerrainCells,
            new Dictionary<string, string>());

    private static TerrainCellCoordinate ToCoordinate(TerrainCellState cell) =>
        new(cell.X, cell.Y, cell.Z);

    private static bool IsScreen(NodeStatus status, string name) =>
        string.Equals(status.Screen, name, StringComparison.Ordinal);

    private static string FormatCell(TerrainCellState cell) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({cell.X},{cell.Y},{cell.Z})={cell.Value}/contents:{cell.Contents}/data:{cell.Data}");

    private static void ValidateRuntime(string runtimeDirectory) {
        if (!Directory.Exists(runtimeDirectory)) {
            throw new DirectoryNotFoundException(
                $"Game runtime directory '{runtimeDirectory}' does not exist.");
        }
        string[] required = [
            "Survivalcraft.exe",
            "Survivalcraft.dll",
            "Survivalcraft.Networking.dll",
            "Content.zip",
            Path.Combine("Mods", "Survivalcraft.TestAutomation.scmod")
        ];
        string[] missing = required
            .Where(path => !File.Exists(Path.Combine(runtimeDirectory, path)))
            .ToArray();
        if (missing.Length > 0) {
            throw new FileNotFoundException(
                $"Runtime '{runtimeDirectory}' is incomplete. Missing: {string.Join(", ", missing)}");
        }
    }

    private static bool IsPathInside(string candidatePath, string parentPath) {
        string candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteCreatedRunDirectory(string runDirectory, string workBase) {
        try {
            string fullRunDirectory = Path.GetFullPath(runDirectory);
            if (!IsPathInside(fullRunDirectory, workBase)
                || string.Equals(
                    fullRunDirectory.TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(workBase).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)) {
                return;
            }
            if (Directory.Exists(fullRunDirectory)) {
                Directory.Delete(fullRunDirectory, recursive: true);
            }
        }
        catch {
            // Test data cleanup must not hide the scenario result or its artifacts.
        }
    }
}
