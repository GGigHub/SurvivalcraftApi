using System.Diagnostics;
using System.Globalization;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record SessionLifecycleMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    MultiplayerTransportKind Transport = MultiplayerTransportKind.Direct,
    int Seed = 20260730,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null);

public sealed record SessionLifecycleMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int CompatibilityRejections,
    int StableIdentityCount,
    long DuplicatePlayerCreatesIgnored,
    long RateLimitedMessages,
    TimeSpan Elapsed);

public sealed class SessionLifecycleMultiplayerScenarioException : Exception {
    public SessionLifecycleMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class SessionLifecycleMultiplayerScenario {
    public const string ScenarioName = "session-lifecycle-multiplayer";
    public const int ScenarioVersion = 3;

    private static readonly string[] NodeRoles = [
        "server", "client-a", "client-b", "client-c", "client-d"
    ];

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-cluster", "process.start(server, clients, relay?)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("require-capabilities", ControlCommandNames.Describe),
        new("configure-identities", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("start-transport-host", "network.startHost"),
        new("compatibility-rejection-matrix", ControlCommandNames.ConfigureCompatibility),
        new("simultaneous-join-storm", "network.connect(client-a,b,c)"),
        new("verify-session-admission", ControlCommandNames.GetSnapshotV2),
        new("replay-player-create", ControlCommandNames.ReplayPlayerCreate),
        new("verify-mod-peer-isolation", ControlCommandNames.RunTestModTrafficBurst),
        new("verify-mod-rate-limit", ControlCommandNames.RunTestModTrafficBurst),
        new("verify-mod-rate-recovery", ControlCommandNames.RunTestModTrafficBurst),
        new("verify-mod-backpressure", ControlCommandNames.SetNetworkFaults),
        new("disconnect-during-player-creation", ControlCommandNames.Disconnect),
        new("recover-player-creation", "network.connect(client-d)"),
        new("verify-mixed-optional-session-isolation", ControlCommandNames.InjectTestModEnvelope),
        new("restore-exact-mod-session-contracts", ControlCommandNames.Disconnect),
        new("simultaneous-reconnect-storm", "network.connect(all)"),
        new("verify-stable-identities", ControlCommandNames.GetSnapshotV2),
        new("supersede-duplicate-identity", ControlCommandNames.ConfigureNode),
        new("verify-session-scoped-cleanup", ControlCommandNames.GetSnapshotV2)
    ];

    public async Task<SessionLifecycleMultiplayerScenarioResult> RunAsync(
        SessionLifecycleMultiplayerScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory, options.Transport);
        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(75);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(12);
        if (stepTimeout <= TimeSpan.Zero || scenarioTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        string runId = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string workBase = Path.GetFullPath(options.WorkDirectory
            ?? Path.Combine(Path.GetTempPath(), "Survivalcraft.TestHost"));
        string runDirectory = Path.Combine(workBase, $"{ScenarioName}-{runId}");
        Directory.CreateDirectory(runDirectory);

        ScenarioRunner runner = new(stepTimeout);
        ScenarioDefinition definition = new(
            ScenarioName,
            ScenarioVersion,
            options.Seed,
            NodeRoles,
            StepDefinitions);
        Dictionary<string, AutomationDescription> capabilities = new(StringComparer.Ordinal);
        Dictionary<string, StateSnapshotV2> snapshots = new(StringComparer.Ordinal);
        Dictionary<string, ActionEventsResult> events = new(StringComparer.Ordinal);
        List<ScenarioAssertionRecordV2> assertions = [];
        Dictionary<string, string> metrics = new(StringComparer.Ordinal);
        Dictionary<string, string[]> logs = new(StringComparer.Ordinal);
        Stopwatch stopwatch = Stopwatch.StartNew();
        MultiplayerCluster? cluster = null;
        Exception? scenarioFailure = null;
        Exception? artifactFailure = null;
        Dictionary<string, int> stableIndexes = new(StringComparer.Ordinal);
        long duplicateCreatesIgnored = 0;
        long rateLimitedMessages = 0;
        int compatibilityRejections = 0;
        NetworkFaultProfile mutationFaults = (options.NetworkFaults
            ?? new NetworkFaultProfile(
                Seed: options.Seed + 50,
                DuplicationRate: 1,
                ReorderRate: 0.5,
                ReorderDelayMilliseconds: 30)) with {
                    TargetPacketTypes = ["PlayerCreatePacket"]
                };
        mutationFaults.Validate();

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
                ClientCount: 4,
                StartupTimeout: stepTimeout));

            await runner.RunAsync(
                "start-cluster",
                "process.start(server, clients, relay?)",
                token => cluster.StartAsync(token),
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "wait-main-menu",
                ControlCommandNames.GetStatus,
                token => Task.WhenAll(MultiplayerGameplayBootstrap.AllNodes(cluster).Select(node =>
                    MultiplayerScenarioOperations.WaitForMainMenuAsync(
                        node,
                        stepTimeout,
                        token))),
                scenarioToken).ConfigureAwait(false);

            RequiredAutomationCapabilities required = new(
                SnapshotSchemaVersion: AutomationProtocol.CurrentSnapshotSchemaVersion,
                Commands: [
                    ControlCommandNames.Describe,
                    ControlCommandNames.ConfigureNode,
                    ControlCommandNames.GetSnapshotV2,
                    ControlCommandNames.Disconnect,
                    ControlCommandNames.SetNetworkFaults,
                    ControlCommandNames.ConfigureCompatibility,
                    ControlCommandNames.ConfigureModChannels,
                    ControlCommandNames.GetCompatibility,
                    ControlCommandNames.GetModChannels,
                    ControlCommandNames.ResetTestModMessages,
                    ControlCommandNames.RunTestModTrafficBurst,
                    ControlCommandNames.InjectTestModEnvelope,
                    ControlCommandNames.ReplayPlayerCreate
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Node,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Player
                ],
                AllowLegacyExecution: true);
            await runner.RunAsync(
                "require-capabilities",
                ControlCommandNames.Describe,
                async token => {
                    foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
                        capabilities[node.Role] = await node.RequireCapabilitiesAsync(required, token)
                            .ConfigureAwait(false);
                    }
                },
                scenarioToken).ConfigureAwait(false);

            string[] identities = Enumerable.Range(0, cluster.Clients.Length)
                .Select(index => CreateDeterministicIdentity(options.Seed, index).ToString("D"))
                .ToArray();
            await runner.RunAsync(
                "configure-identities",
                ControlCommandNames.ConfigureNode,
                async token => {
                    List<Task> configurations = [
                        cluster.Server.SendAsync<ConfigureNodeCommand, ControlAck>(
                            ControlCommandNames.ConfigureNode,
                            new ConfigureNodeCommand("server", options.Seed),
                            cancellationToken: token)
                    ];
                    configurations.AddRange(cluster.Clients.Select((client, index) =>
                        (Task)client.SendAsync<ConfigureNodeCommand, ControlAck>(
                            ControlCommandNames.ConfigureNode,
                            new ConfigureNodeCommand(
                                client.Role,
                                options.Seed,
                                NetworkIdentity: identities[index]),
                            cancellationToken: token)));
                    await Task.WhenAll(configurations).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);

            _ = await runner.RunAsync(
                "create-server-world",
                ControlCommandNames.CreateWorld,
                token => cluster.Server.SendAsync<CreateWorldCommand, CreateWorldResult>(
                    ControlCommandNames.CreateWorld,
                    new CreateWorldCommand(
                        $"SessionLifecycle-{options.Transport}-{options.Seed}",
                        options.Seed.ToString(CultureInfo.InvariantCulture)),
                    timeout: stepTimeout,
                    cancellationToken: token),
                result => result.DirectoryName,
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "enter-server-world",
                ControlCommandNames.EnterWorld,
                async token => {
                    NodeStatus status = await MultiplayerScenarioOperations.WaitForWorldEntryAsync(
                        cluster.Server,
                        serverSide: true,
                        stepTimeout,
                        token).ConfigureAwait(false);
                    if (string.Equals(status.Screen, "PlayerScreen", StringComparison.Ordinal)) {
                        await MultiplayerScenarioOperations.EnterWorldWhenReadyAsync(
                            cluster.Server,
                            "Host",
                            stepTimeout,
                            token).ConfigureAwait(false);
                    }
                    _ = await cluster.Server.WaitForStatusAsync(
                        value => value.ProjectLoaded
                            && value.IsServerRunning
                            && value.MainPlayerReady
                            && string.Equals(value.Screen, "GameScreen", StringComparison.Ordinal),
                        stepTimeout,
                        "Server did not become playable.",
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
                "compatibility-rejection-matrix",
                ControlCommandNames.ConfigureCompatibility,
                async token => {
                    GameNodeProcess probe = cluster.Clients[3];
                    CompatibilityStatus baseline =
                        await probe.SendAsync<EmptyCommand, CompatibilityStatus>(
                            ControlCommandNames.GetCompatibility,
                            new EmptyCommand(),
                            cancellationToken: token).ConfigureAwait(false);
                    CompatibilityModDescriptor[] extraMods = [
                        .. baseline.Mods,
                        new CompatibilityModDescriptor(
                            "test.session-probe",
                            "1.0.0",
                            baseline.ApiVersion)
                    ];

                    async Task RejectAsync(
                        string caseName,
                        ConfigureCompatibilityCommand compatibility,
                        string expectedCode,
                        ConfigureModChannelsCommand? channels = null) {
                        _ = await probe.SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                            ControlCommandNames.ConfigureCompatibility,
                            compatibility,
                            cancellationToken: token).ConfigureAwait(false);
                        _ = await probe.SendAsync<ConfigureModChannelsCommand, ModChannelStatus>(
                            ControlCommandNames.ConfigureModChannels,
                            channels ?? new ConfigureModChannelsCommand(ClearOverride: true),
                            cancellationToken: token).ConfigureAwait(false);
                        await cluster.ConnectClientAsync(probe, token).ConfigureAwait(false);
                        CompatibilityStatus rejected =
                            await WaitForCompatibilityRejectionAsync(
                                probe,
                                expectedCode,
                                stepTimeout,
                                token).ConfigureAwait(false);
                        _ = await cluster.Server.WaitForStatusAsync(
                            status => status.ServerSessionCount == 0,
                            stepTimeout,
                            $"Server retained rejected compatibility case '{caseName}'.",
                            token).ConfigureAwait(false);
                        metrics[$"compatibility.{caseName}.code"] =
                            rejected.PrimaryCode ?? string.Empty;
                        compatibilityRejections++;
                    }

                    await RejectAsync(
                        "protocol",
                        new ConfigureCompatibilityCommand(
                            ProtocolRevision: baseline.ProtocolRevision + 1),
                        "ProtocolMismatch").ConfigureAwait(false);
                    await RejectAsync(
                        "api",
                        new ConfigureCompatibilityCommand(ApiVersion: "0.0-session-test"),
                        "ApiVersionMismatch").ConfigureAwait(false);
                    await RejectAsync(
                        "capability",
                        new ConfigureCompatibilityCommand(Capabilities: 0),
                        "MissingCapabilities").ConfigureAwait(false);
                    await RejectAsync(
                        "mod",
                        new ConfigureCompatibilityCommand(Mods: extraMods),
                        "ExtraClientMod").ConfigureAwait(false);
                    await RejectAsync(
                        "mod-channel",
                        new ConfigureCompatibilityCommand(ClearOverride: true),
                        "MissingClientChannel",
                        new ConfigureModChannelsCommand(Channels: [])).ConfigureAwait(false);

                    _ = await probe.SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                        ControlCommandNames.ConfigureCompatibility,
                        new ConfigureCompatibilityCommand(ClearOverride: true),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await probe.SendAsync<ConfigureModChannelsCommand, ModChannelStatus>(
                        ControlCommandNames.ConfigureModChannels,
                        new ConfigureModChannelsCommand(ClearOverride: true),
                        cancellationToken: token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "compatibility-rejection-transport-parity",
                ["server", "client-d"],
                "Protocol, API, capability, mod and mod-channel rejection codes converged over the selected transport."));

            GameNodeProcess[] initialClients = cluster.Clients.Take(3).ToArray();
            await runner.RunAsync(
                "simultaneous-join-storm",
                "network.connect(client-a,b,c)",
                async token => {
                    await Task.WhenAll(initialClients.Select((client, index) =>
                        MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                            cluster,
                            client,
                            $"Learner{(char)('A' + index)}",
                            stepTimeout,
                            token))).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == initialClients.Length,
                        stepTimeout,
                        "Server did not retain all simultaneous join sessions.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);

            StateSnapshotV2 admitted = await runner.RunAsync(
                "verify-session-admission",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForNetworkSnapshotAsync(
                    cluster.Server,
                    snapshot => snapshot.Network?.Sessions?.Length == 3
                        && snapshot.Network.Sessions.All(session =>
                            session.CompatibilityState == "Accepted"
                            && session.AdmissionState == "Accepted"
                            && session.PlayerIndex >= 0)
                        && snapshot.Network.ModChannel?.CatalogFingerprint.Length > 0,
                    stepTimeout,
                    "Server did not expose three admitted sessions.",
                    token),
                snapshot => $"sessions={snapshot.Network?.Sessions?.Length}",
                scenarioToken).ConfigureAwait(false);
            snapshots["server-admitted"] = admitted;
            EnsureUniqueSessions(admitted, expectedCount: 3);
            assertions.Add(Passed(
                "simultaneous-join-admission",
                ["server", .. initialClients.Select(client => client.Role)],
                "Three concurrent joins completed compatibility, typed admission and player assignment."));

            duplicateCreatesIgnored = await runner.RunAsync(
                "replay-player-create",
                ControlCommandNames.ReplayPlayerCreate,
                async token => {
                    await Task.WhenAll(initialClients.Select((client, index) =>
                        client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                            ControlCommandNames.SetNetworkFaults,
                            new SetNetworkFaultsCommand(mutationFaults with {
                                Seed = unchecked(mutationFaults.Seed + index)
                            }),
                            cancellationToken: token))).ConfigureAwait(false);
                    await Task.WhenAll(initialClients.Select(client =>
                        client.SendAsync<ReplayPlayerCreateCommand, ReplayPlayerCreateResult>(
                            ControlCommandNames.ReplayPlayerCreate,
                            new ReplayPlayerCreateCommand(3),
                            cancellationToken: token))).ConfigureAwait(false);
                    StateSnapshotV2 snapshot = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.SessionLifecycle?.DuplicatePlayerCreateIgnored >= 9,
                        stepTimeout,
                        "Server did not ignore all replayed player-create packets.",
                        token).ConfigureAwait(false);
                    snapshots["server-player-create-replay"] = snapshot;
                    foreach (GameNodeProcess client in initialClients) {
                        StateSnapshotV2 faultEvidence = await WaitForNetworkSnapshotAsync(
                            client,
                            value => value.Network?.FaultPending == 0,
                            stepTimeout,
                            $"{client.Role} did not drain player-create mutation traffic.",
                            token).ConfigureAwait(false);
                        snapshots[$"{client.Role}-player-create-mutation"] = faultEvidence;
                        _ = await client.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                            ControlCommandNames.SetNetworkFaults,
                            new SetNetworkFaultsCommand(new NetworkFaultProfile(
                                Seed: options.Seed + 60)),
                            cancellationToken: token).ConfigureAwait(false);
                    }
                    if (mutationFaults.LossRate > 0) {
                        GameNodeProcess lossSentry = initialClients[0];
                        _ = await lossSentry.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                            ControlCommandNames.SetNetworkFaults,
                            new SetNetworkFaultsCommand(new NetworkFaultProfile(
                                Seed: mutationFaults.Seed + 10_000,
                                LossRate: 1,
                                TargetPacketTypes: ["PlayerCreatePacket"])),
                            cancellationToken: token).ConfigureAwait(false);
                        _ = await lossSentry
                            .SendAsync<ReplayPlayerCreateCommand, ReplayPlayerCreateResult>(
                                ControlCommandNames.ReplayPlayerCreate,
                                new ReplayPlayerCreateCommand(1),
                                cancellationToken: token).ConfigureAwait(false);
                        StateSnapshotV2 forcedLoss = await WaitForNetworkSnapshotAsync(
                            lossSentry,
                            value => value.Network is {
                                FaultDropped: >= 1,
                                FaultPending: 0
                            },
                            stepTimeout,
                            "Deterministic player-create loss sentry did not observe a drop.",
                            token).ConfigureAwait(false);
                        snapshots["client-a-player-create-loss-sentry"] = forcedLoss;
                        _ = await lossSentry.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                            ControlCommandNames.SetNetworkFaults,
                            new SetNetworkFaultsCommand(new NetworkFaultProfile(
                                Seed: options.Seed + 61)),
                            cancellationToken: token).ConfigureAwait(false);
                    }
                    EnsureUniqueSessions(snapshot, expectedCount: 3);
                    if (snapshot.Players.Length != 4) {
                        throw new InvalidOperationException(
                            $"Player-create replay changed player cardinality to {snapshot.Players.Length}; expected 4.");
                    }
                    return snapshot.Network!.SessionLifecycle!.DuplicatePlayerCreateIgnored;
                },
                value => value.ToString(CultureInfo.InvariantCulture),
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "duplicate-player-create-idempotent",
                NodeRoles.Take(4).ToArray(),
                $"Ignored {duplicateCreatesIgnored} duplicate PlayerCreate packets without allocating players."));

            await runner.RunAsync(
                "verify-mod-peer-isolation",
                ControlCommandNames.RunTestModTrafficBurst,
                async token => {
                    await ResetModMetricsAsync(cluster.Server, initialClients, token)
                        .ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(1100), token).ConfigureAwait(false);
                    await Task.WhenAll(initialClients.Select((client, index) =>
                        client.SendAsync<RunTestModTrafficBurstCommand, TestModTrafficBurstResult>(
                            ControlCommandNames.RunTestModTrafficBurst,
                            new RunTestModTrafficBurstCommand(
                                96,
                                PayloadCharacters: 64,
                                CorrelationPrefix: $"isolation-{index}"),
                            cancellationToken: token))).ConfigureAwait(false);
                    StateSnapshotV2 snapshot = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ModChannel is {
                            Delivered: >= 288,
                            RateLimited: 0,
                            ActivePeerStates: 3
                        },
                        stepTimeout,
                        "Per-session mod traffic did not remain isolated.",
                        token).ConfigureAwait(false);
                    snapshots["server-mod-isolation"] = snapshot;
                },
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "mod-channel-per-session-isolation",
                NodeRoles.Take(4).ToArray(),
                "Three peers each delivered 96 messages without sharing the 128-message limiter."));

            rateLimitedMessages = await runner.RunAsync(
                "verify-mod-rate-limit",
                ControlCommandNames.RunTestModTrafficBurst,
                async token => {
                    await Task.Delay(TimeSpan.FromMilliseconds(1100), token).ConfigureAwait(false);
                    await ResetModMetricsAsync(cluster.Server, initialClients, token)
                        .ConfigureAwait(false);
                    _ = await initialClients[0]
                        .SendAsync<RunTestModTrafficBurstCommand, TestModTrafficBurstResult>(
                            ControlCommandNames.RunTestModTrafficBurst,
                            new RunTestModTrafficBurstCommand(
                                160,
                                PayloadCharacters: 64,
                                CorrelationPrefix: "rate-limit"),
                            cancellationToken: token).ConfigureAwait(false);
                    StateSnapshotV2 snapshot = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ModChannel is {
                            Received: >= 160,
                            Delivered: >= 128,
                            RateLimited: >= 32
                        },
                        stepTimeout,
                        "Mod channel did not expose its production rate limit.",
                        token).ConfigureAwait(false);
                    snapshots["server-mod-rate-limit"] = snapshot;
                    return snapshot.Network!.ModChannel!.RateLimited;
                },
                value => value.ToString(CultureInfo.InvariantCulture),
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "verify-mod-rate-recovery",
                ControlCommandNames.RunTestModTrafficBurst,
                async token => {
                    await Task.Delay(TimeSpan.FromMilliseconds(1100), token).ConfigureAwait(false);
                    await ResetModMetricsAsync(cluster.Server, initialClients, token)
                        .ConfigureAwait(false);
                    _ = await initialClients[0]
                        .SendAsync<RunTestModTrafficBurstCommand, TestModTrafficBurstResult>(
                            ControlCommandNames.RunTestModTrafficBurst,
                            new RunTestModTrafficBurstCommand(
                                32,
                                PayloadCharacters: 64,
                                CorrelationPrefix: "rate-recovery"),
                            cancellationToken: token).ConfigureAwait(false);
                    StateSnapshotV2 snapshot = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ModChannel is {
                            Delivered: >= 32,
                            RateLimited: 0
                        },
                        stepTimeout,
                        "Mod channel limiter did not recover after its window elapsed.",
                        token).ConfigureAwait(false);
                    snapshots["server-mod-rate-recovery"] = snapshot;
                },
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "mod-channel-rate-limit-and-recovery",
                ["server", "client-a"],
                $"Observed {rateLimitedMessages} rate-limited messages, then a clean recovery window."));

            await runner.RunAsync(
                "verify-mod-backpressure",
                ControlCommandNames.SetNetworkFaults,
                async token => {
                    NetworkFaultProfile profile = new(
                        Seed: options.Seed + 70,
                        BandwidthBytesPerSecond: 256,
                        TargetPacketTypes: ["ModMessagePacket"]);
                    _ = await initialClients[0].SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(profile),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await initialClients[0]
                        .SendAsync<RunTestModTrafficBurstCommand, TestModTrafficBurstResult>(
                            ControlCommandNames.RunTestModTrafficBurst,
                            new RunTestModTrafficBurstCommand(
                                8,
                                PayloadCharacters: 512,
                                CorrelationPrefix: "backpressure"),
                            cancellationToken: token).ConfigureAwait(false);
                    StateSnapshotV2 queued = await WaitForNetworkSnapshotAsync(
                        initialClients[0],
                        value => value.Network?.FaultPending > 0
                            && value.Network.FaultTransmissionsByType?
                                .GetValueOrDefault("ModMessagePacket") > 0,
                        stepTimeout,
                        "Targeted mod traffic did not enter the bandwidth queue.",
                        token).ConfigureAwait(false);
                    snapshots["client-a-mod-backpressure"] = queued;
                    StateSnapshotV2 drained = await WaitForNetworkSnapshotAsync(
                        initialClients[0],
                        value => value.Network?.FaultPending == 0,
                        stepTimeout,
                        "Targeted mod traffic bandwidth queue did not drain.",
                        token).ConfigureAwait(false);
                    snapshots["client-a-mod-drained"] = drained;
                    _ = await initialClients[0].SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(new NetworkFaultProfile(Seed: options.Seed + 71)),
                        cancellationToken: token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "mod-channel-targeted-backpressure",
                ["server", "client-a"],
                "Only ModMessagePacket traffic entered a bounded bandwidth queue and drained."));

            GameNodeProcess creationClient = cluster.Clients[3];
            await runner.RunAsync(
                "disconnect-during-player-creation",
                ControlCommandNames.Disconnect,
                async token => {
                    _ = await cluster.Server.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(new NetworkFaultProfile(
                            Seed: options.Seed + 80,
                            LossRate: 1,
                            TargetPacketTypes: ["EntityLoadPacket"])),
                        cancellationToken: token).ConfigureAwait(false);
                    await cluster.ConnectClientAsync(creationClient, token).ConfigureAwait(false);
                    _ = await MultiplayerScenarioOperations.WaitForWorldEntryAsync(
                        creationClient,
                        serverSide: false,
                        stepTimeout,
                        token).ConfigureAwait(false);
                    await MultiplayerScenarioOperations.EnterWorldWhenReadyAsync(
                        creationClient,
                        "InterruptedLearner",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    StateSnapshotV2 creating = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => FindSession(value, identities[3]) is {
                            PlayerCreateRequests: >= 1,
                            PlayerIndex: >= 0
                        },
                        stepTimeout,
                        "Server did not reach player creation before interruption.",
                        token).ConfigureAwait(false);
                    snapshots["server-player-creation-interrupted"] = creating;
                    _ = await creationClient.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: token).ConfigureAwait(false);
                    StateSnapshotV2 cleaned = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ServerSessionCount == 3
                            && FindSession(value, identities[3]) == null
                            && value.Players.All(player => player.NetworkIdentity != identities[3]),
                        stepTimeout,
                        "Interrupted player creation was not cleaned up.",
                        token).ConfigureAwait(false);
                    snapshots["server-player-creation-cleaned"] = cleaned;
                    _ = await cluster.Server.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                        ControlCommandNames.SetNetworkFaults,
                        new SetNetworkFaultsCommand(new NetworkFaultProfile(Seed: options.Seed + 81)),
                        cancellationToken: token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "disconnect-during-player-entity-creation",
                ["server", creationClient.Role],
                "A deterministic EntityLoad blackhole interrupted creation and server cleanup removed the session/player."));

            await runner.RunAsync(
                "recover-player-creation",
                "network.connect(client-d)",
                token => MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                    cluster,
                    creationClient,
                    "RecoveredLearner",
                    stepTimeout,
                    token),
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 beforeReconnect = await CaptureNetworkAsync(
                cluster.Server,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-before-reconnect-storm"] = beforeReconnect;
            EnsureUniqueSessions(beforeReconnect, expectedCount: 4);
            stableIndexes = ToIdentityIndexMap(beforeReconnect);
            Dictionary<string, string> oldSessionIds = await CaptureClientSessionIdsAsync(
                cluster.Clients,
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "verify-mixed-optional-session-isolation",
                ControlCommandNames.InjectTestModEnvelope,
                async token => {
                    CompatibilityStatus baseline =
                        await cluster.Clients[0]
                            .SendAsync<EmptyCommand, CompatibilityStatus>(
                                ControlCommandNames.GetCompatibility,
                                new EmptyCommand(),
                                cancellationToken: token).ConfigureAwait(false);
                    const string testAutomationPackage = "survivalcraft.testautomation";
                    CompatibilityModDescriptor[] optionalMods = baseline.Mods
                        .Select(mod => string.Equals(
                                mod.PackageName,
                                testAutomationPackage,
                                StringComparison.OrdinalIgnoreCase)
                            ? mod with { NetworkScope = "Optional" }
                            : mod)
                        .ToArray();
                    if (!optionalMods.Any(mod => string.Equals(
                            mod.PackageName,
                            testAutomationPackage,
                            StringComparison.OrdinalIgnoreCase))) {
                        throw new InvalidOperationException(
                            "TestAutomation mod is missing from the session compatibility baseline.");
                    }
                    CompatibilityModDescriptor[] withoutTestMod = baseline.Mods
                        .Where(mod => !string.Equals(
                            mod.PackageName,
                            testAutomationPackage,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    await Task.WhenAll(cluster.Clients.Select(client =>
                        client.SendAsync<DisconnectCommand, ControlAck>(
                            ControlCommandNames.Disconnect,
                            new DisconnectCommand(),
                            cancellationToken: token))).ConfigureAwait(false);
                    _ = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ServerSessionCount == 0
                            && value.Network.ModChannel?.ActivePeerStates == 0,
                        stepTimeout,
                        "Server retained state before the mixed optional-mod matrix.",
                        token).ConfigureAwait(false);

                    _ = await cluster.Server
                        .SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                            ControlCommandNames.ConfigureCompatibility,
                            new ConfigureCompatibilityCommand(Mods: optionalMods),
                            cancellationToken: token).ConfigureAwait(false);
                    for (int index = 0; index < cluster.Clients.Length; index++) {
                        GameNodeProcess client = cluster.Clients[index];
                        bool hasTestMod = index < cluster.Clients.Length - 1;
                        _ = await client
                            .SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                                ControlCommandNames.ConfigureCompatibility,
                                new ConfigureCompatibilityCommand(
                                    Mods: hasTestMod ? optionalMods : withoutTestMod),
                                cancellationToken: token).ConfigureAwait(false);
                        _ = await client.SendAsync<ConfigureModChannelsCommand, ModChannelStatus>(
                            ControlCommandNames.ConfigureModChannels,
                            hasTestMod
                                ? new ConfigureModChannelsCommand(ClearOverride: true)
                                : new ConfigureModChannelsCommand(Channels: []),
                            cancellationToken: token).ConfigureAwait(false);
                    }

                    await Task.WhenAll(cluster.Clients.Select((client, index) =>
                        MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                            cluster,
                            client,
                            $"Optional{(char)('A' + index)}",
                            stepTimeout,
                            token))).ConfigureAwait(false);
                    StateSnapshotV2 mixed = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ServerSessionCount == 4
                            && value.Network.Sessions?.Count(session =>
                                session.NegotiatedModChannelCount == 1) == 3
                            && value.Network.Sessions?.Count(session =>
                                session.NegotiatedModChannelCount == 0) == 1,
                        stepTimeout,
                        "Server did not expose three negotiated and one isolated mod session.",
                        token).ConfigureAwait(false);
                    snapshots["server-mixed-optional-sessions"] = mixed;

                    ModChannelStatus serverChannels =
                        await cluster.Server.SendAsync<EmptyCommand, ModChannelStatus>(
                            ControlCommandNames.GetModChannels,
                            new EmptyCommand(),
                            cancellationToken: token).ConfigureAwait(false);
                    ModChannelStatus[] clientChannels = await Task.WhenAll(
                        cluster.Clients.Select(client =>
                            client.SendAsync<EmptyCommand, ModChannelStatus>(
                                ControlCommandNames.GetModChannels,
                                new EmptyCommand(),
                                cancellationToken: token))).ConfigureAwait(false);
                    if (serverChannels.NegotiatedChannelCount != 3
                        || clientChannels.Take(3).Any(status =>
                            status.NegotiatedChannelCount != 1)
                        || clientChannels[3].NegotiatedChannelCount != 0) {
                        throw new InvalidOperationException(
                            "Mixed optional-mod session channel counts did not remain peer-scoped.");
                    }

                    await ModChannelIsolationAssertions.VerifyAsync(
                        cluster.Server,
                        cluster.Clients[3],
                        metrics,
                        $"optional.session.{options.Transport.ToString().ToLowerInvariant()}",
                        options.Seed,
                        stepTimeout,
                        token,
                        expectedBroadcastRecipients: 3,
                        targetNetworkIdentity: identities[3],
                        expectedRecipients: cluster.Clients.Take(3).ToArray()).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "mixed-optional-mod-session-isolation",
                NodeRoles,
                "Three peers negotiated the mod channel while the absent peer joined with zero channels; broadcasts and raw ingress remained session-scoped."));

            await runner.RunAsync(
                "restore-exact-mod-session-contracts",
                ControlCommandNames.Disconnect,
                async token => {
                    await Task.WhenAll(cluster.Clients.Select(client =>
                        client.SendAsync<DisconnectCommand, ControlAck>(
                            ControlCommandNames.Disconnect,
                            new DisconnectCommand(),
                            cancellationToken: token))).ConfigureAwait(false);
                    StateSnapshotV2 empty = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ServerSessionCount == 0
                            && value.Network.ModChannel?.ActivePeerStates == 0,
                        stepTimeout,
                        "Server retained mixed optional-mod session state.",
                        token).ConfigureAwait(false);
                    snapshots["server-mixed-optional-cleaned"] = empty;
                    _ = await cluster.Server
                        .SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                            ControlCommandNames.ConfigureCompatibility,
                            new ConfigureCompatibilityCommand(ClearOverride: true),
                            cancellationToken: token).ConfigureAwait(false);
                    foreach (GameNodeProcess client in cluster.Clients) {
                        _ = await client
                            .SendAsync<ConfigureCompatibilityCommand, CompatibilityStatus>(
                                ControlCommandNames.ConfigureCompatibility,
                                new ConfigureCompatibilityCommand(ClearOverride: true),
                                cancellationToken: token).ConfigureAwait(false);
                        _ = await client.SendAsync<ConfigureModChannelsCommand, ModChannelStatus>(
                            ControlCommandNames.ConfigureModChannels,
                            new ConfigureModChannelsCommand(ClearOverride: true),
                            cancellationToken: token).ConfigureAwait(false);
                    }
                },
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "simultaneous-reconnect-storm",
                "network.connect(all)",
                async token => {
                    StateSnapshotV2 empty = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ServerSessionCount == 0
                            && value.Network.ModChannel?.ActivePeerStates == 0,
                        stepTimeout,
                        "Server retained session-scoped state after simultaneous disconnect.",
                        token).ConfigureAwait(false);
                    snapshots["server-reconnect-storm-empty"] = empty;
                    await Task.WhenAll(cluster.Clients.Select((client, index) =>
                        MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                            cluster,
                            client,
                            $"Reconnected{(char)('A' + index)}",
                            stepTimeout,
                            token))).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);

            StateSnapshotV2 reconnected = await runner.RunAsync(
                "verify-stable-identities",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForNetworkSnapshotAsync(
                    cluster.Server,
                    value => value.Network?.ServerSessionCount == 4
                        && value.Network.Sessions?.All(session =>
                            session.AdmissionState == "Accepted"
                            && session.PlayerIndex >= 0) == true,
                    stepTimeout,
                    "Server did not admit the simultaneous reconnect storm.",
                    token),
                value => $"sessions={value.Network?.ServerSessionCount}",
                scenarioToken).ConfigureAwait(false);
            snapshots["server-after-reconnect-storm"] = reconnected;
            EnsureUniqueSessions(reconnected, expectedCount: 4);
            Dictionary<string, int> reconnectedIndexes = ToIdentityIndexMap(reconnected);
            if (!stableIndexes.OrderBy(item => item.Key).SequenceEqual(
                    reconnectedIndexes.OrderBy(item => item.Key))) {
                throw new InvalidOperationException(
                    "Stable identity to PlayerIndex mapping changed across the reconnect storm.");
            }
            Dictionary<string, string> newSessionIds = await CaptureClientSessionIdsAsync(
                cluster.Clients,
                scenarioToken).ConfigureAwait(false);
            if (oldSessionIds.Any(item => newSessionIds.GetValueOrDefault(item.Key) == item.Value)) {
                throw new InvalidOperationException(
                    "A reconnect reused transport session identity instead of creating session-scoped state.");
            }
            assertions.Add(Passed(
                "simultaneous-reconnect-stable-identity",
                NodeRoles,
                "Four peers reconnected concurrently with stable player indexes and fresh session IDs."));

            await runner.RunAsync(
                "supersede-duplicate-identity",
                ControlCommandNames.ConfigureNode,
                async token => {
                    _ = await creationClient.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 3,
                        stepTimeout,
                        "Server did not remove client D before identity takeover.",
                        token).ConfigureAwait(false);
                    _ = await creationClient.SendAsync<ConfigureNodeCommand, ControlAck>(
                        ControlCommandNames.ConfigureNode,
                        new ConfigureNodeCommand(
                            creationClient.Role,
                            options.Seed,
                            NetworkIdentity: identities[0]),
                        cancellationToken: token).ConfigureAwait(false);
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        creationClient,
                        "IdentityTakeover",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    _ = await cluster.Clients[0].WaitForStatusAsync(
                        status => !status.IsClientRunning
                            && string.Equals(status.Screen, "MainMenuScreen", StringComparison.Ordinal),
                        stepTimeout,
                        "Superseded client A did not disconnect.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 superseded = await WaitForNetworkSnapshotAsync(
                cluster.Server,
                value => value.Network?.SessionLifecycle?.Superseded >= 1
                    && value.Network.ServerSessionCount == 3,
                stepTimeout,
                "Server did not expose typed duplicate-identity supersession.",
                scenarioToken).ConfigureAwait(false);
            snapshots["server-duplicate-identity-superseded"] = superseded;
            EnsureUniqueSessions(superseded, expectedCount: 3);
            if (superseded.Network!.Sessions!.Count(session =>
                    session.NetworkIdentity == identities[0]) != 1) {
                throw new InvalidOperationException(
                    "Duplicate identity takeover left more than one active identity session.");
            }
            assertions.Add(Passed(
                "duplicate-identity-latest-session-wins",
                ["server", "client-a", "client-d"],
                "The latest admitted connection synchronously replaced the old session without duplicate players."));

            await runner.RunAsync(
                "verify-session-scoped-cleanup",
                ControlCommandNames.GetSnapshotV2,
                async token => {
                    foreach (GameNodeProcess client in cluster.Clients.Skip(1)) {
                        NodeStatus status = await client.GetStatusAsync(token).ConfigureAwait(false);
                        if (status.IsClientRunning) {
                            _ = await client.SendAsync<DisconnectCommand, ControlAck>(
                                ControlCommandNames.Disconnect,
                                new DisconnectCommand(),
                                cancellationToken: token).ConfigureAwait(false);
                        }
                    }
                    StateSnapshotV2 final = await WaitForNetworkSnapshotAsync(
                        cluster.Server,
                        value => value.Network?.ServerSessionCount == 0
                            && value.Network.ModChannel?.ActivePeerStates == 0,
                        stepTimeout,
                        "Session or mod-channel peer state survived final disconnect.",
                        token).ConfigureAwait(false);
                    snapshots["server-final-session-cleanup"] = final;
                },
                scenarioToken).ConfigureAwait(false);
            assertions.Add(Passed(
                "session-scoped-state-nonpersistent",
                NodeRoles,
                "Compatibility, admission, deduplication and limiter peer state were absent after disconnect."));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(cluster, snapshots).ConfigureAwait(false);
                foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
                    ScenarioResourceMetrics.Capture(metrics, node);
                    logs[node.Role] = node.Logs.ToArray();
                }
                if (cluster.Relay != null) {
                    logs["relay"] = cluster.Relay.Logs.ToArray();
                    metrics["relay.port"] = cluster.Relay.Port.ToString(CultureInfo.InvariantCulture);
                }
                await cluster.DisposeAsync().ConfigureAwait(false);
            }
            metrics["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
            metrics["transport"] = options.Transport.ToString();
            metrics["stableIdentityCount"] = stableIndexes.Count.ToString(CultureInfo.InvariantCulture);
            metrics["duplicatePlayerCreatesIgnored"] =
                duplicateCreatesIgnored.ToString(CultureInfo.InvariantCulture);
            metrics["rateLimitedMessages"] =
                rateLimitedMessages.ToString(CultureInfo.InvariantCulture);
            metrics["compatibilityRejections"] =
                compatibilityRejections.ToString(CultureInfo.InvariantCulture);
            try {
                ScenarioReplayV2 replay = new(
                    ScenarioName,
                    ScenarioVersion,
                    options.Seed,
                    runtimeDirectory,
                    options.Transport.ToString(),
                    ControlProtocol.CurrentVersion,
                    AutomationProtocol.CurrentApiVersion,
                    AutomationProtocol.CurrentSnapshotSchemaVersion,
                    mutationFaults);
                ScenarioTopologyV2 topology = new(
                    options.Transport.ToString(),
                    "server",
                    cluster?.Clients.Select(client => client.Role).ToArray()
                        ?? NodeRoles.Skip(1).ToArray(),
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
                        [],
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
            throw new SessionLifecycleMultiplayerScenarioException(
                $"Session lifecycle multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new SessionLifecycleMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new SessionLifecycleMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            compatibilityRejections,
            stableIndexes.Count,
            duplicateCreatesIgnored,
            rateLimitedMessages,
            stopwatch.Elapsed);
    }

    private static async Task<CompatibilityStatus> WaitForCompatibilityRejectionAsync(
        GameNodeProcess client,
        string expectedCode,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        CompatibilityStatus? lastCompatibility = null;
        NodeStatus? lastStatus = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            lastCompatibility =
                await client.SendAsync<EmptyCommand, CompatibilityStatus>(
                    ControlCommandNames.GetCompatibility,
                    new EmptyCommand(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            lastStatus = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (lastCompatibility.Accepted == false
                && string.Equals(
                    lastCompatibility.PrimaryCode,
                    expectedCode,
                    StringComparison.Ordinal)
                && !lastStatus.IsClientRunning
                && !lastStatus.ProjectLoaded) {
                return lastCompatibility;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Client did not reject compatibility with code '{expectedCode}'. "
            + $"LastCompatibility={lastCompatibility}; LastStatus={lastStatus}."
            + Environment.NewLine
            + client.GetRecentLogText());
    }

    private static async Task ResetModMetricsAsync(
        GameNodeProcess server,
        IEnumerable<GameNodeProcess> clients,
        CancellationToken cancellationToken) {
        await Task.WhenAll(new[] { server }.Concat(clients).Select(node =>
            node.SendAsync<EmptyCommand, TestModMessagesStatus>(
                ControlCommandNames.ResetTestModMessages,
                new EmptyCommand(),
                cancellationToken: cancellationToken))).ConfigureAwait(false);
    }

    private static Task<StateSnapshotV2> CaptureNetworkAsync(
        GameNodeProcess node,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command([
                SnapshotDomain.Network,
                SnapshotDomain.Players
            ]),
            cancellationToken: cancellationToken);

    private static async Task<StateSnapshotV2> WaitForNetworkSnapshotAsync(
        GameNodeProcess node,
        Func<StateSnapshotV2, bool> predicate,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureNetworkAsync(node, cancellationToken).ConfigureAwait(false);
            if (predicate(last)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException($"{failureMessage} LastNetwork={last?.Network}.");
    }

    private static NetworkSessionSnapshotV2? FindSession(
        StateSnapshotV2 snapshot,
        string networkIdentity) =>
        snapshot.Network?.Sessions?.FirstOrDefault(session =>
            string.Equals(session.NetworkIdentity, networkIdentity, StringComparison.OrdinalIgnoreCase));

    private static void EnsureUniqueSessions(StateSnapshotV2 snapshot, int expectedCount) {
        NetworkSessionSnapshotV2[] sessions = snapshot.Network?.Sessions
            ?? throw new InvalidOperationException("Network sessions are absent from Snapshot v2.");
        if (sessions.Length != expectedCount) {
            throw new InvalidOperationException(
                $"Expected {expectedCount} sessions, observed {sessions.Length}.");
        }
        if (sessions.Select(session => session.SessionId).Distinct(StringComparer.Ordinal).Count()
                != expectedCount
            || sessions.Select(session => session.NetworkIdentity)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != expectedCount
            || sessions.Select(session => session.PlayerIndex).Distinct().Count() != expectedCount) {
            throw new InvalidOperationException(
                "Active sessions contain duplicate session IDs, identities or player indexes.");
        }
    }

    private static Dictionary<string, int> ToIdentityIndexMap(StateSnapshotV2 snapshot) =>
        (snapshot.Network?.Sessions
            ?? throw new InvalidOperationException("Network sessions are absent."))
        .Where(session => !string.IsNullOrWhiteSpace(session.NetworkIdentity))
        .ToDictionary(
            session => session.NetworkIdentity!,
            session => session.PlayerIndex,
            StringComparer.OrdinalIgnoreCase);

    private static async Task<Dictionary<string, string>> CaptureClientSessionIdsAsync(
        IEnumerable<GameNodeProcess> clients,
        CancellationToken cancellationToken) {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (GameNodeProcess client in clients) {
            StateSnapshotV2 snapshot = await CaptureNetworkAsync(client, cancellationToken)
                .ConfigureAwait(false);
            string sessionId = snapshot.Network?.Sessions?.SingleOrDefault()?.SessionId
                ?? throw new InvalidOperationException(
                    $"Client '{client.Role}' has no local session snapshot.");
            result[client.Role] = sessionId;
        }
        return result;
    }

    private static Guid CreateDeterministicIdentity(int seed, int index) {
        byte[] bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), seed);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), index + 1);
        bytes[8] = 0x53;
        bytes[9] = 0x43;
        bytes[10] = 0x4E;
        bytes[11] = 0x45;
        bytes[12] = 0x54;
        bytes[15] = checked((byte)(index + 1));
        return new Guid(bytes);
    }

    private static ScenarioAssertionRecordV2 Passed(
        string name,
        string[] roles,
        string detail) =>
        new(name, "passed", roles, detail);

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        IDictionary<string, StateSnapshotV2> snapshots) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            try {
                snapshots[$"{node.Role}-final"] =
                    await MultiplayerScenarioOperations
                        .CaptureAfterApplicationReliabilityIdleAsync(
                            node,
                            token => CaptureNetworkAsync(node, token),
                            TimeSpan.FromSeconds(10),
                            CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Process logs remain the fallback diagnostic artifact.
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
            // Preserve an isolated run directory when cleanup is unsafe or unavailable.
        }
    }
}
