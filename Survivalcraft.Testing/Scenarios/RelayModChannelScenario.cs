using System.Diagnostics;
using System.Globalization;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record RelayModChannelScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed = 20260728,
    NetworkFaultProfile? NetworkFaults = null,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false);

public sealed record RelayModChannelScenarioResult(
    string ArtifactDirectory,
    string RoomCode,
    long DeduplicatedFrames,
    TimeSpan Elapsed);

public sealed class RelayModChannelScenarioException : Exception {
    public RelayModChannelScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class RelayModChannelScenario {
    public const string ScenarioName = "relay-mod-channel";
    public const int ScenarioVersion = 1;

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-relay-server", "process.start(relay)"),
        new("start-game-nodes", "process.start(server, client)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("create-relay-room", ControlCommandNames.StartRelayHost),
        new("connect-relay-client", ControlCommandNames.ConnectRelay),
        new("enter-relay-client-world", ControlCommandNames.EnterWorld),
        new("verify-channel-negotiation", ControlCommandNames.GetModChannels),
        new("exchange-and-deduplicate", ControlCommandNames.SendTestModMessage),
        new("isolate-handler-failure", ControlCommandNames.SendTestModMessage),
        new("disconnect-and-clean-session", ControlCommandNames.Disconnect),
        new("reconnect-relay-client", ControlCommandNames.ConnectRelay),
        new("verify-channel-after-reconnect", ControlCommandNames.SendTestModMessage)
    ];

    public async Task<RelayModChannelScenarioResult> RunAsync(
        RelayModChannelScenarioOptions options,
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
                BaseLatencyMilliseconds: 8,
                JitterMilliseconds: 2)).Validate();

        string runId = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string workBase = Path.GetFullPath(options.WorkDirectory
            ?? Path.Combine(Path.GetTempPath(), "Survivalcraft.TestHost"));
        string runDirectory = Path.Combine(workBase, $"{ScenarioName}-{runId}");
        Directory.CreateDirectory(runDirectory);

        ScenarioDefinition definition = new(
            ScenarioName,
            ScenarioVersion,
            options.Seed,
            ["relay", "server", "client"],
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
        RelayServerProcess? relay = null;
        GameNodeProcess? server = null;
        GameNodeProcess? client = null;
        string roomCode = string.Empty;
        long deduplicatedFrames = 0;
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
            relay = new RelayServerProcess(new RelayServerOptions(
                runtimeDirectory,
                StartupTimeout: stepTimeout));
            await StepActionAsync(
                "start-relay-server",
                "process.start(relay)",
                () => relay.StartAsync(scenarioToken)).ConfigureAwait(false);
            metrics["relay.port"] = relay.Port.ToString(CultureInfo.InvariantCulture);

            server = new GameNodeProcess(new GameNodeOptions(
                "relay-server",
                runtimeDirectory,
                Path.Combine(runDirectory, "server"),
                StartupTimeout: stepTimeout));
            client = new GameNodeProcess(new GameNodeOptions(
                "relay-client",
                runtimeDirectory,
                Path.Combine(runDirectory, "client"),
                StartupTimeout: stepTimeout));
            await StepActionAsync(
                "start-game-nodes",
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
                        new ConfigureNodeCommand("relay-server", options.Seed),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await client.SendAsync<ConfigureNodeCommand, ControlAck>(
                        ControlCommandNames.ConfigureNode,
                        new ConfigureNodeCommand("relay-client", options.Seed),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            _ = await StepAsync(
                "create-server-world",
                ControlCommandNames.CreateWorld,
                () => server.SendAsync<CreateWorldCommand, CreateWorldResult>(
                    ControlCommandNames.CreateWorld,
                    new CreateWorldCommand(
                        $"Relay-{options.Seed}",
                        options.Seed.ToString(CultureInfo.InvariantCulture)),
                    timeout: stepTimeout,
                    cancellationToken: scenarioToken),
                result => result.DirectoryName).ConfigureAwait(false);

            await StepActionAsync(
                "enter-server-world",
                ControlCommandNames.EnterWorld,
                () => EnsureServerPlayingAsync(
                    server,
                    "RelayHost",
                    stepTimeout,
                    scenarioToken)).ConfigureAwait(false);

            RelayStatus hostRelayStatus = await StepAsync(
                "create-relay-room",
                ControlCommandNames.StartRelayHost,
                async () => {
                    relay.ThrowIfUnhealthy();
                    _ = await server.SendAsync<StartRelayHostCommand, ControlAck>(
                        ControlCommandNames.StartRelayHost,
                        new StartRelayHostCommand("127.0.0.1", relay.Port),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    return await WaitForRelayStatusAsync(
                        server,
                        status => status.HostConnected
                            && status.HostCodeStatus == "Ready"
                            && !string.IsNullOrWhiteSpace(status.RoomCode),
                        stepTimeout,
                        "Relay host did not receive a room code.",
                        scenarioToken).ConfigureAwait(false);
                },
                status => status.RoomCode).ConfigureAwait(false);
            roomCode = hostRelayStatus.RoomCode;
            metrics["relay.roomCode"] = roomCode;

            await StepActionAsync(
                "connect-relay-client",
                ControlCommandNames.ConnectRelay,
                async () => {
                    relay.ThrowIfUnhealthy();
                    _ = await client.SendAsync<ConnectRelayCommand, ControlAck>(
                        ControlCommandNames.ConnectRelay,
                        new ConnectRelayCommand("127.0.0.1", relay.Port, roomCode),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForWorldEntryAsync(
                        client,
                        serverSide: false,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "enter-relay-client-world",
                ControlCommandNames.EnterWorld,
                () => EnsureClientPlayingAsync(
                    client,
                    "RelayLearner",
                    stepTimeout,
                    scenarioToken)).ConfigureAwait(false);

            await StepActionAsync(
                "verify-channel-negotiation",
                ControlCommandNames.GetModChannels,
                async () => {
                    CompatibilityStatus compatibility =
                        await client.SendAsync<EmptyCommand, CompatibilityStatus>(
                            ControlCommandNames.GetCompatibility,
                            new EmptyCommand(),
                            cancellationToken: scenarioToken).ConfigureAwait(false);
                    if (compatibility.Accepted != true || compatibility.PrimaryCode != "None") {
                        throw new InvalidOperationException(
                            $"Relay client compatibility was not accepted: {compatibility}.");
                    }
                    ModChannelStatus serverCatalog =
                        await server.SendAsync<EmptyCommand, ModChannelStatus>(
                            ControlCommandNames.GetModChannels,
                            new EmptyCommand(),
                            cancellationToken: scenarioToken).ConfigureAwait(false);
                    ModChannelStatus clientCatalog =
                        await client.SendAsync<EmptyCommand, ModChannelStatus>(
                            ControlCommandNames.GetModChannels,
                            new EmptyCommand(),
                            cancellationToken: scenarioToken).ConfigureAwait(false);
                    if (serverCatalog.Channels.Length == 0
                        || !string.Equals(
                            serverCatalog.Fingerprint,
                            clientCatalog.Fingerprint,
                            StringComparison.Ordinal)) {
                        throw new InvalidOperationException(
                            "Relay peers did not negotiate the same non-empty mod channel catalog.");
                    }
                    metrics["modChannel.fingerprint"] = serverCatalog.Fingerprint;
                    snapshots["relay-initial-server"] = await CaptureAsync(
                        server,
                        scenarioToken).ConfigureAwait(false);
                    snapshots["relay-initial-client"] = await CaptureAsync(
                        client,
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            deduplicatedFrames = await StepAsync(
                "exchange-and-deduplicate",
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

                    NetworkFaultProfile duplicateEveryFrame = new(
                        Seed: unchecked(options.Seed + 401),
                        DuplicationRate: 1);
                    _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(duplicateEveryFrame),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    string duplicateCorrelation = $"relay-duplicate-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "relay-frame-must-be-deduplicated",
                            CorrelationId: duplicateCorrelation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    TestModMessagesStatus serverMessages = await WaitForTestModStateAsync(
                        server,
                        status => CountTestMessages(status, duplicateCorrelation) == 1
                            && status.Channel.Metrics.Duplicates >= 1,
                        stepTimeout,
                        "Relay host did not deduplicate the duplicated mod frame.",
                        scenarioToken).ConfigureAwait(false);
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, duplicateCorrelation) == 1,
                        stepTimeout,
                        "Relay client did not receive the deduplicated echo.",
                        scenarioToken).ConfigureAwait(false);

                    _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(new NetworkFaultProfile(
                            Seed: unchecked(options.Seed + 402))),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    string broadcastCorrelation = $"relay-broadcast-{options.Seed}";
                    TestModSendResult broadcast =
                        await server.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                            ControlCommandNames.SendTestModMessage,
                            new SendTestModMessageCommand(
                                "relay-server-broadcast",
                                Broadcast: true,
                                CorrelationId: broadcastCorrelation),
                            cancellationToken: scenarioToken).ConfigureAwait(false);
                    if (broadcast.RecipientCount != 1) {
                        throw new InvalidOperationException(
                            $"Expected one relay broadcast recipient, got {broadcast.RecipientCount}.");
                    }
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, broadcastCorrelation) == 1,
                        stepTimeout,
                        "Relay client did not receive the server broadcast.",
                        scenarioToken).ConfigureAwait(false);
                    return serverMessages.Channel.Metrics.Duplicates;
                },
                count => count.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            metrics["modChannel.relayDuplicates"] =
                deduplicatedFrames.ToString(CultureInfo.InvariantCulture);

            await StepActionAsync(
                "isolate-handler-failure",
                ControlCommandNames.SendTestModMessage,
                async () => {
                    string failureCorrelation = $"relay-handler-failure-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "isolated-relay-handler-failure",
                            FailHandler: true,
                            CorrelationId: failureCorrelation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    TestModMessagesStatus failed = await WaitForTestModStateAsync(
                        server,
                        status => CountTestMessages(status, failureCorrelation) == 1
                            && status.Channel.Metrics.HandlerErrors >= 1,
                        stepTimeout,
                        "Relay host did not isolate the requested handler failure.",
                        scenarioToken).ConfigureAwait(false);
                    metrics["modChannel.relayHandlerErrors"] =
                        failed.Channel.Metrics.HandlerErrors.ToString(CultureInfo.InvariantCulture);

                    string recoveryCorrelation = $"relay-handler-recovery-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "relay-handler-remains-operational",
                            CorrelationId: recoveryCorrelation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, recoveryCorrelation) == 1,
                        stepTimeout,
                        "Relay mod channel did not recover after handler failure.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "disconnect-and-clean-session",
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
                        "Relay client did not return to MainMenu after disconnect.",
                        scenarioToken).ConfigureAwait(false);
                    _ = await WaitForRelayStatusAsync(
                        server,
                        status => status.HostConnected && status.HostSessionCount == 0,
                        stepTimeout,
                        "Relay host retained the disconnected client session.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "reconnect-relay-client",
                ControlCommandNames.ConnectRelay,
                async () => {
                    relay.ThrowIfUnhealthy();
                    _ = await client.SendAsync<ConnectRelayCommand, ControlAck>(
                        ControlCommandNames.ConnectRelay,
                        new ConnectRelayCommand("127.0.0.1", relay.Port, roomCode),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForWorldEntryAsync(
                        client,
                        serverSide: false,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    await EnsureClientPlayingAsync(
                        client,
                        "RelayLearner",
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    _ = await WaitForRelayStatusAsync(
                        server,
                        status => status.HostConnected && status.HostSessionCount == 1,
                        stepTimeout,
                        "Relay host did not expose the reconnected client session.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            await StepActionAsync(
                "verify-channel-after-reconnect",
                ControlCommandNames.SendTestModMessage,
                async () => {
                    NetworkFaultProfile clientFaults = faultProfile with {
                        Seed = unchecked(faultProfile.Seed + 1)
                    };
                    _ = await server.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(faultProfile),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(clientFaults),
                        cancellationToken: scenarioToken).ConfigureAwait(false);

                    string correlation = $"relay-reconnect-{options.Seed}";
                    _ = await client.SendAsync<SendTestModMessageCommand, TestModSendResult>(
                        ControlCommandNames.SendTestModMessage,
                        new SendTestModMessageCommand(
                            "relay-channel-survived-reconnect",
                            CorrelationId: correlation),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await WaitForTestModStateAsync(
                        client,
                        status => CountTestMessages(status, correlation) == 1,
                        stepTimeout,
                        "Relay mod channel did not deliver after reconnect.",
                        scenarioToken).ConfigureAwait(false);
                    snapshots["relay-reconnected-server"] = await CaptureAsync(
                        server,
                        scenarioToken).ConfigureAwait(false);
                    snapshots["relay-reconnected-client"] = await CaptureAsync(
                        client,
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            metrics["elapsedMilliseconds"] =
                stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
            metrics["seed"] = options.Seed.ToString(CultureInfo.InvariantCulture);
            metrics["stepCount"] = steps.Count.ToString(CultureInfo.InvariantCulture);
            metrics["passedStepCount"] = steps.Count(step => step.Status == "passed")
                .ToString(CultureInfo.InvariantCulture);

            Dictionary<string, string[]> logs = new(StringComparer.Ordinal);
            if (relay != null) {
                logs["relay"] = relay.Logs.ToArray();
            }
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
                null,
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
            if (relay != null) {
                await relay.DisposeAsync().ConfigureAwait(false);
            }
            if (!options.KeepData && !IsPathInside(artifactDirectory, runDirectory)) {
                TryDeleteCreatedRunDirectory(runDirectory, workBase);
            }
        }

        if (scenarioFailure != null) {
            if (artifactFailure != null) {
                scenarioFailure = new AggregateException(scenarioFailure, artifactFailure);
            }
            throw new RelayModChannelScenarioException(
                $"Relay mod channel scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new IOException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactFailure);
        }
        if (string.IsNullOrWhiteSpace(roomCode) || deduplicatedFrames <= 0) {
            throw new InvalidOperationException(
                "Relay scenario completed without its required room or deduplication result.");
        }
        return new RelayModChannelScenarioResult(
            artifactDirectory,
            roomCode,
            deduplicatedFrames,
            stopwatch.Elapsed);
    }

    private static async Task<RelayStatus> WaitForRelayStatusAsync(
        GameNodeProcess node,
        Func<RelayStatus, bool> predicate,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        RelayStatus? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await node.SendAsync<EmptyCommand, RelayStatus>(
                ControlCommandNames.GetRelayStatus,
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

    private static async Task<TestModMessagesStatus> WaitForTestModStateAsync(
        GameNodeProcess node,
        Func<TestModMessagesStatus, bool> predicate,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken) {
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

    private static Task<NodeStatus> WaitForMainMenuAsync(
        GameNodeProcess node,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        node.WaitForStatusAsync(
            status => status.ContentLoaded && IsScreen(status, "MainMenuScreen"),
            timeout,
            "Game content did not reach MainMenu.",
            cancellationToken);

    private static Task<NodeStatus> WaitForWorldEntryAsync(
        GameNodeProcess node,
        bool serverSide,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        node.WaitForStatusAsync(
            status => IsScreen(status, "PlayerScreen")
                || (status.ProjectLoaded
                    && IsScreen(status, "GameScreen")
                    && (status.MainPlayerReady || !serverSide && status.NetworkState == "Playing")),
            timeout,
            $"Node '{node.Role}' did not reach player selection or a playable game.",
            cancellationToken);

    private static async Task EnsureServerPlayingAsync(
        GameNodeProcess server,
        string playerName,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        NodeStatus entry = await WaitForWorldEntryAsync(
            server,
            serverSide: true,
            timeout,
            cancellationToken).ConfigureAwait(false);
        if (IsScreen(entry, "PlayerScreen")) {
            await EnterWorldWhenReadyAsync(
                server,
                playerName,
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        _ = await server.WaitForStatusAsync(
            status => status.ProjectLoaded
                && status.IsServerRunning
                && status.MainPlayerReady
                && IsScreen(status, "GameScreen"),
            timeout,
            "Relay server node did not finish entering its world.",
            cancellationToken).ConfigureAwait(false);
    }

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
            "Relay client did not reach NetworkState.Playing with a player entity.",
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

    private static Task<StateSnapshot> CaptureAsync(
        GameNodeProcess node,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotCommand, StateSnapshot>(
            ControlCommandNames.GetSnapshot,
            new GetSnapshotCommand(IncludeEntities: true),
            cancellationToken: cancellationToken);

    private static bool IsScreen(NodeStatus status, string name) =>
        string.Equals(status.Screen, name, StringComparison.Ordinal);

    private static void ValidateRuntime(string runtimeDirectory) {
        if (!Directory.Exists(runtimeDirectory)) {
            throw new DirectoryNotFoundException(
                $"Game runtime directory '{runtimeDirectory}' does not exist.");
        }
        string[] required = [
            "Survivalcraft.exe",
            "Survivalcraft.dll",
            "Survivalcraft.Networking.dll",
            "Survivalcraft.RelayServer.dll",
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
            // Test data cleanup must not hide the scenario result or artifacts.
        }
    }
}
