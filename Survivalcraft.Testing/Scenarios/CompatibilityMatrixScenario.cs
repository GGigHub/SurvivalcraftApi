using System.Diagnostics;
using System.Globalization;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record CompatibilityMatrixScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed = 20260728,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false);

public sealed record CompatibilityMatrixScenarioResult(
    string ArtifactDirectory,
    IReadOnlyDictionary<string, string> RejectionCodes,
    TimeSpan Elapsed);

public sealed class CompatibilityMatrixScenarioException : Exception {
    public CompatibilityMatrixScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class CompatibilityMatrixScenario {
    public const string ScenarioName = "compatibility-matrix";
    public const int ScenarioVersion = 3;

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-nodes", "process.start(server, client)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("start-server-world", ControlCommandNames.CreateWorld),
        new("reject-protocol-mismatch", ControlCommandNames.ConfigureCompatibility),
        new("reject-api-mismatch", ControlCommandNames.ConfigureCompatibility),
        new("reject-capability-mismatch", ControlCommandNames.ConfigureCompatibility),
        new("reject-mod-mismatch", ControlCommandNames.ConfigureCompatibility),
        new("reject-mod-channel-mismatch", ControlCommandNames.ConfigureModChannels),
        new("blackhole-compatibility-hello", ControlCommandNames.SetNetworkFaults),
        new("blackhole-compatibility-result", ControlCommandNames.SetNetworkFaults),
        new("blackhole-mod-channel-catalog", ControlCommandNames.SetNetworkFaults),
        new("blackhole-mod-channel-catalog-result", ControlCommandNames.SetNetworkFaults),
        new("blackhole-session-connect", ControlCommandNames.SetNetworkFaults),
        new("blackhole-session-admission-result", ControlCommandNames.SetNetworkFaults),
        new("recover-with-matching-manifest", ControlCommandNames.ConfigureCompatibility),
        new("stop-nodes", ControlCommandNames.Disconnect)
    ];

    public async Task<CompatibilityMatrixScenarioResult> RunAsync(
        CompatibilityMatrixScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory);

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(8);
        if (stepTimeout <= TimeSpan.Zero || scenarioTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeouts must be positive.");
        }

        string runId = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string workBase = Path.GetFullPath(options.WorkDirectory
            ?? Path.Combine(Path.GetTempPath(), "Survivalcraft.TestHost"));
        string runDirectory = Path.Combine(workBase, $"{ScenarioName}-{runId}");
        Directory.CreateDirectory(runDirectory);

        NetworkFaultProfile noFaults = new(Seed: options.Seed);
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
            noFaults);
        List<ScenarioStepRecord> steps = [];
        Dictionary<string, StateSnapshot> snapshots = new(StringComparer.Ordinal);
        Dictionary<string, string> metrics = new(StringComparer.Ordinal);
        Dictionary<string, string> rejectionCodes = new(StringComparer.Ordinal);
        Stopwatch stopwatch = Stopwatch.StartNew();
        GameNodeProcess? server = null;
        GameNodeProcess? client = null;
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
            _ = await StepAsync(
                name,
                command,
                async () => {
                    await action().ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
        }

        async Task<CompatibilityStatus> RunRejectionAsync(
            string caseName,
            ConfigureCompatibilityCommand? configuration,
            string expectedCode,
            ConfigureModChannelsCommand? channelConfiguration = null) {
            if (server == null || client == null) {
                throw new InvalidOperationException("Scenario nodes are not running.");
            }
            if (configuration != null) {
                _ = await client.SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                    ControlCommandNames.ConfigureCompatibility,
                    configuration,
                    cancellationToken: scenarioToken).ConfigureAwait(false);
            }
            if (channelConfiguration != null) {
                _ = await client.SendAsync<ConfigureModChannelsCommand, ModChannelStatus>(
                    ControlCommandNames.ConfigureModChannels,
                    channelConfiguration,
                    cancellationToken: scenarioToken).ConfigureAwait(false);
            }
            _ = await client.SendAsync<ConnectClientCommand, ControlAck>(
                ControlCommandNames.ConnectClient,
                new ConnectClientCommand("127.0.0.1", server.GamePort),
                cancellationToken: scenarioToken).ConfigureAwait(false);

            CompatibilityStatus rejected = await WaitForCompatibilityAsync(
                client,
                expectedCode,
                accepted: false,
                requireProjectLoaded: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            _ = await server.WaitForStatusAsync(
                status => status.ServerSessionCount == 0,
                stepTimeout,
                $"Server retained the rejected '{caseName}' session.",
                scenarioToken).ConfigureAwait(false);

            rejectionCodes[caseName] = rejected.PrimaryCode ?? string.Empty;
            metrics[$"{caseName}.code"] = rejected.PrimaryCode ?? string.Empty;
            metrics[$"{caseName}.summary"] = rejected.Summary ?? string.Empty;
            snapshots[$"{caseName}-rejected"] =
                await client.SendAsync<GetSnapshotCommand, StateSnapshot>(
                    ControlCommandNames.GetSnapshot,
                    new GetSnapshotCommand(IncludeEntities: false),
                    cancellationToken: scenarioToken).ConfigureAwait(false);
            return rejected;
        }

        async Task<string> RunHandshakeBlackholeAsync(
            string caseName,
            string packetType,
            bool injectOnServer,
            bool admissionPhase) {
            if (server == null || client == null) {
                throw new InvalidOperationException("Scenario nodes are not running.");
            }
            _ = await client.SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                ControlCommandNames.ConfigureCompatibility,
                new ConfigureCompatibilityCommand(ClearOverride: true),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            _ = await client.SendAsync<ConfigureModChannelsCommand, ModChannelStatus>(
                ControlCommandNames.ConfigureModChannels,
                new ConfigureModChannelsCommand(ClearOverride: true),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            _ = await server.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(noFaults),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(noFaults),
                cancellationToken: scenarioToken).ConfigureAwait(false);

            GameNodeProcess faultNode = injectOnServer ? server : client;
            NetworkFaultProfile blackhole = new(
                Seed: options.Seed + 100 + rejectionCodes.Count,
                LossRate: 1,
                TargetPacketTypes: [packetType]);
            _ = await faultNode.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(blackhole),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            _ = await client.SendAsync<ConnectClientCommand, ControlAck>(
                ControlCommandNames.ConnectClient,
                new ConnectClientCommand("127.0.0.1", server.GamePort),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            _ = await client.WaitForStatusAsync(
                status => status.IsClientRunning
                    && !string.IsNullOrWhiteSpace(status.SessionId),
                stepTimeout,
                $"Client did not start blackholed handshake phase '{caseName}'.",
                scenarioToken).ConfigureAwait(false);

            string code;
            string summary;
            if (admissionPhase) {
                NodeStatus timedOut = await WaitForAdmissionTimeoutAsync(
                    client,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
                code = timedOut.AdmissionCode ?? string.Empty;
                summary = $"admissionState={timedOut.AdmissionState}";
            }
            else {
                CompatibilityStatus timedOut = await WaitForCompatibilityAsync(
                    client,
                    "HandshakeTimeout",
                    accepted: false,
                    requireProjectLoaded: false,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
                code = timedOut.PrimaryCode ?? string.Empty;
                summary = timedOut.Summary ?? string.Empty;
            }
            _ = await server.WaitForStatusAsync(
                status => status.ServerSessionCount == 0,
                stepTimeout,
                $"Server retained blackholed handshake phase '{caseName}'.",
                scenarioToken).ConfigureAwait(false);
            NodeStatus faultStatus = await faultNode.GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);
            if (faultStatus.NetworkFaults.Dropped < 1) {
                throw new InvalidOperationException(
                    $"Targeted blackhole '{caseName}' did not drop {packetType}.");
            }

            rejectionCodes[caseName] = code;
            metrics[$"{caseName}.packetType"] = packetType;
            metrics[$"{caseName}.code"] = code;
            metrics[$"{caseName}.summary"] = summary;
            metrics[$"{caseName}.droppedPackets"] =
                faultStatus.NetworkFaults.Dropped.ToString(CultureInfo.InvariantCulture);
            snapshots[$"{caseName}-rejected"] =
                await client.SendAsync<GetSnapshotCommand, StateSnapshot>(
                    ControlCommandNames.GetSnapshot,
                    new GetSnapshotCommand(IncludeEntities: false),
                    cancellationToken: scenarioToken).ConfigureAwait(false);

            _ = await faultNode.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(noFaults),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            return code;
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

            CompatibilityStatus baseline =
                await client.SendAsync<EmptyCommand, CompatibilityStatus>(
                    ControlCommandNames.GetCompatibility,
                    new EmptyCommand(),
                    cancellationToken: scenarioToken).ConfigureAwait(false);

            await StepActionAsync(
                "start-server-world",
                ControlCommandNames.CreateWorld,
                async () => {
                    _ = await server.SendAsync<CreateWorldCommand, CreateWorldResult>(
                        ControlCommandNames.CreateWorld,
                        new CreateWorldCommand(
                            $"Compatibility-{options.Seed}",
                            options.Seed.ToString(CultureInfo.InvariantCulture)),
                        timeout: stepTimeout,
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    NodeStatus entry = await WaitForWorldEntryAsync(
                        server,
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
                        "Server did not become playable for compatibility tests.",
                        scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            _ = await StepAsync(
                "reject-protocol-mismatch",
                ControlCommandNames.ConfigureCompatibility,
                () => RunRejectionAsync(
                    "protocol",
                    new ConfigureCompatibilityCommand(
                        ProtocolRevision: baseline.ProtocolRevision + 1),
                    "ProtocolMismatch"),
                result => result.PrimaryCode).ConfigureAwait(false);

            _ = await StepAsync(
                "reject-api-mismatch",
                ControlCommandNames.ConfigureCompatibility,
                () => RunRejectionAsync(
                    "api",
                    new ConfigureCompatibilityCommand(ApiVersion: "0.0-test"),
                    "ApiVersionMismatch"),
                result => result.PrimaryCode).ConfigureAwait(false);

            _ = await StepAsync(
                "reject-capability-mismatch",
                ControlCommandNames.ConfigureCompatibility,
                () => RunRejectionAsync(
                    "capability",
                    new ConfigureCompatibilityCommand(Capabilities: 0),
                    "MissingCapabilities"),
                result => result.PrimaryCode).ConfigureAwait(false);

            CompatibilityModDescriptor[] extraMods = [
                .. baseline.Mods,
                new CompatibilityModDescriptor("test.client.only", "1.0.0", baseline.ApiVersion)
            ];
            _ = await StepAsync(
                "reject-mod-mismatch",
                ControlCommandNames.ConfigureCompatibility,
                () => RunRejectionAsync(
                    "mod",
                    new ConfigureCompatibilityCommand(Mods: extraMods),
                    "ExtraClientMod"),
                result => result.PrimaryCode).ConfigureAwait(false);

            _ = await StepAsync(
                "reject-mod-channel-mismatch",
                ControlCommandNames.ConfigureModChannels,
                () => RunRejectionAsync(
                    "mod-channel",
                    new ConfigureCompatibilityCommand(ClearOverride: true),
                    "MissingClientChannel",
                    new ConfigureModChannelsCommand(Channels: [])),
                result => result.PrimaryCode).ConfigureAwait(false);

            (string Step, string Case, string Packet, bool Server, bool Admission)[] phases = [
                ("blackhole-compatibility-hello", "timeout-compatibility-hello", "CompatibilityHelloPacket", false, false),
                ("blackhole-compatibility-result", "timeout-compatibility-result", "CompatibilityResultPacket", true, false),
                ("blackhole-mod-channel-catalog", "timeout-mod-channel-catalog", "ModChannelCatalogPacket", false, false),
                ("blackhole-mod-channel-catalog-result", "timeout-mod-channel-catalog-result", "ModChannelCatalogResultPacket", true, false),
                ("blackhole-session-connect", "timeout-session-connect", "ConnectPacket", false, true),
                ("blackhole-session-admission-result", "timeout-session-admission-result", "SessionAdmissionResultPacket", true, true)
            ];
            foreach ((string step, string caseName, string packet, bool onServer, bool admission)
                     in phases) {
                _ = await StepAsync(
                    step,
                    ControlCommandNames.SetNetworkFaults,
                    () => RunHandshakeBlackholeAsync(
                        caseName,
                        packet,
                        onServer,
                        admission),
                    result => result).ConfigureAwait(false);
            }

            _ = await StepAsync(
                "recover-with-matching-manifest",
                ControlCommandNames.ConfigureCompatibility,
                async () => {
                    _ = await client.SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                        ControlCommandNames.ConfigureCompatibility,
                        new ConfigureCompatibilityCommand(ClearOverride: true),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await client.SendAsync<ConfigureModChannelsCommand, ModChannelStatus>(
                        ControlCommandNames.ConfigureModChannels,
                        new ConfigureModChannelsCommand(ClearOverride: true),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await client.SendAsync<ConnectClientCommand, ControlAck>(
                        ControlCommandNames.ConnectClient,
                        new ConnectClientCommand("127.0.0.1", server.GamePort),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    CompatibilityStatus accepted = await WaitForCompatibilityAsync(
                        client,
                        "None",
                        accepted: true,
                        requireProjectLoaded: true,
                        stepTimeout,
                        scenarioToken).ConfigureAwait(false);
                    snapshots["matching-accepted"] =
                        await client.SendAsync<GetSnapshotCommand, StateSnapshot>(
                            ControlCommandNames.GetSnapshot,
                            new GetSnapshotCommand(IncludeEntities: false),
                            cancellationToken: scenarioToken).ConfigureAwait(false);
                    return accepted;
                },
                result => result.State).ConfigureAwait(false);

            await StepActionAsync(
                "stop-nodes",
                ControlCommandNames.Disconnect,
                async () => {
                    _ = await client.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                    _ = await server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 0,
                        stepTimeout,
                        "Server retained the accepted recovery session.",
                        scenarioToken).ConfigureAwait(false);
                    _ = await server.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: scenarioToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            metrics["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
            metrics["seed"] = options.Seed.ToString(CultureInfo.InvariantCulture);
            metrics["rejectionCount"] = rejectionCodes.Count.ToString(CultureInfo.InvariantCulture);

            Dictionary<string, string[]> logs = new(StringComparer.Ordinal);
            if (server != null) logs["server"] = server.Logs.ToArray();
            if (client != null) logs["client"] = client.Logs.ToArray();
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

            if (client != null) await client.DisposeAsync().ConfigureAwait(false);
            if (server != null) await server.DisposeAsync().ConfigureAwait(false);
            if (!options.KeepData && !IsPathInside(artifactDirectory, runDirectory)) {
                TryDeleteCreatedRunDirectory(runDirectory, workBase);
            }
        }

        if (scenarioFailure != null) {
            if (artifactFailure != null) {
                scenarioFailure = new AggregateException(scenarioFailure, artifactFailure);
            }
            throw new CompatibilityMatrixScenarioException(
                $"Compatibility matrix scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new IOException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactFailure);
        }
        if (rejectionCodes.Count != 11) {
            throw new InvalidOperationException(
                $"Scenario completed with {rejectionCodes.Count} rejection cases instead of 11.");
        }
        return new CompatibilityMatrixScenarioResult(
            artifactDirectory,
            rejectionCodes,
            stopwatch.Elapsed);
    }

    private static async Task<CompatibilityStatus> WaitForCompatibilityAsync(
        GameNodeProcess client,
        string expectedCode,
        bool accepted,
        bool requireProjectLoaded,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        CompatibilityStatus? lastCompatibility = null;
        NodeStatus? lastNode = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            lastCompatibility =
                await client.SendAsync<EmptyCommand, CompatibilityStatus>(
                    ControlCommandNames.GetCompatibility,
                    new EmptyCommand(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            lastNode = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            bool codeMatches = string.Equals(
                lastCompatibility.PrimaryCode ?? "None",
                expectedCode,
                StringComparison.Ordinal);
            bool connectionMatches = accepted
                ? lastNode.IsClientRunning
                    && (!requireProjectLoaded || lastNode.ProjectLoaded)
                : !lastNode.IsClientRunning && !lastNode.ProjectLoaded;
            if (lastCompatibility.Accepted == accepted
                && codeMatches
                && connectionMatches) {
                return lastCompatibility;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Client compatibility did not become accepted={accepted}, code={expectedCode}. "
            + $"LastCompatibility={lastCompatibility}; LastNode={lastNode}.{Environment.NewLine}"
            + client.GetRecentLogText());
    }

    private static async Task<NodeStatus> WaitForAdmissionTimeoutAsync(
        GameNodeProcess client,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        NodeStatus? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(
                    last.AdmissionCode,
                    "AdmissionTimeout",
                    StringComparison.Ordinal)
                && !last.IsClientRunning
                && !last.ProjectLoaded) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Client session admission did not time out. LastStatus={last}."
            + Environment.NewLine
            + client.GetRecentLogText());
    }

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
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        node.WaitForStatusAsync(
            status => IsScreen(status, "PlayerScreen")
                || (status.ProjectLoaded
                    && status.MainPlayerReady
                    && IsScreen(status, "GameScreen")),
            timeout,
            $"Node '{node.Role}' did not reach player selection or game.",
            cancellationToken);

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
            // Cleanup must not hide the scenario outcome or diagnostics.
        }
    }
}
