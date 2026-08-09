using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record CreativeFlyReplayPlan(
    SetCreativeFlyCommand HostEnable,
    SetCreativeFlyCommand AuthorityRejection,
    SetCreativeFlyCommand Enable,
    SetCreativeFlyCommand Disable,
    RunCreativeFlyRevisionBurstCommand RevisionBurst,
    SetCreativeFlyCommand PersistenceEnable);

public sealed record CreativeFlyMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    CreativeFlyReplayPlan? ReplayPlan = null);

public sealed record CreativeFlyMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
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
    public const int ScenarioVersion = 2;
    public const string FaultPacketType = "PlayerCreativeFlyPacket";

    private static readonly string[] NodeRoles = [
        "server",
        "client-a",
        "client-b",
        "client-c"
    ];

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-cluster", "process.start(server, client-a, client-b, client-c, relay?)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("require-v2-capabilities", ControlCommandNames.Describe),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("start-transport-host", "network.startHost"),
        new("connect-initial-clients", "network.connect(client-a, client-b)"),
        new("resolve-actor", ControlCommandNames.GetStatus),
        new("reject-non-owner-action", ControlCommandNames.SetCreativeFly),
        new("reset-events", ControlCommandNames.ResetEvents),
        new("execute-host-enable", ControlCommandNames.SetCreativeFly),
        new("verify-host-enable-events", ControlCommandNames.GetEvents),
        new("verify-host-enable-state", ControlCommandNames.GetSnapshotV2),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("set-server-survival", ControlCommandNames.SetEnvironmentFixture),
        new("execute-authority-rejection", ControlCommandNames.SetCreativeFly),
        new("verify-authority-rejection-events", ControlCommandNames.GetEvents),
        new("verify-authority-rejection-state", ControlCommandNames.GetSnapshotV2),
        new("restore-server-creative", ControlCommandNames.SetEnvironmentFixture),
        new("execute-enable", ControlCommandNames.SetCreativeFly),
        new("verify-enable-events", ControlCommandNames.GetEvents),
        new("verify-enable-state", ControlCommandNames.GetSnapshotV2),
        new("execute-disable", ControlCommandNames.SetCreativeFly),
        new("verify-disable-events", ControlCommandNames.GetEvents),
        new("verify-disable-state", ControlCommandNames.GetSnapshotV2),
        new("execute-revision-burst", ControlCommandNames.RunCreativeFlyRevisionBurst),
        new("verify-revision-burst-events", ControlCommandNames.GetEvents),
        new("verify-revision-burst-state", ControlCommandNames.GetSnapshotV2),
        new("verify-network-faults", ControlCommandNames.GetSnapshotV2),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("execute-persistence-enable", ControlCommandNames.SetCreativeFly),
        new("verify-persistence-enable-events", ControlCommandNames.GetEvents),
        new("verify-persistence-enable-state", ControlCommandNames.GetSnapshotV2),
        new("connect-late-client", "network.connect(client-c)"),
        new("verify-late-join-state", ControlCommandNames.GetSnapshotV2),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("reconnect-actor", "network.connect(client-a)"),
        new("verify-reconnect-state", ControlCommandNames.GetSnapshotV2),
        new("save-and-stop-world", ControlCommandNames.SaveWorld),
        new("reload-saved-world", ControlCommandNames.ReloadWorld),
        new("restart-transport-host", "network.startHost"),
        new("reconnect-persisted-actor", "network.connect(client-a)"),
        new("verify-server-persistence", ControlCommandNames.GetSnapshotV2),
        new("connect-persistence-observer", "network.connect(client-c)"),
        new("verify-client-persistence", ControlCommandNames.GetSnapshotV2)
    ];

    public static NetworkFaultProfile CreateDefaultNetworkFaults(int seed) =>
        new(
            Seed: unchecked(seed + 607),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18,
            TargetPacketTypes: [FaultPacketType]);

    public static CreativeFlyReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        (string Role, string Command)[] expected = [
            ("server", ControlCommandNames.SetCreativeFly),
            ("client-a", ControlCommandNames.SetCreativeFly),
            ("client-a", ControlCommandNames.SetCreativeFly),
            ("client-a", ControlCommandNames.SetCreativeFly),
            ("server", ControlCommandNames.RunCreativeFlyRevisionBurst),
            ("client-a", ControlCommandNames.SetCreativeFly)
        ];
        if (records.Count != expected.Length) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly {expected.Length} recorded actions.");
        }
        for (int index = 0; index < records.Count; index++) {
            if (!string.Equals(records[index].Role, expected[index].Role, StringComparison.Ordinal)
                || !string.Equals(records[index].Command, expected[index].Command, StringComparison.Ordinal)) {
                throw new InvalidDataException(
                    $"Replay action {index} must be {expected[index].Role}:{expected[index].Command}, "
                    + $"got {records[index].Role}:{records[index].Command}.");
            }
        }
        CreativeFlyReplayPlan plan = new(
            DeserializeAction<SetCreativeFlyCommand>(records[0]),
            DeserializeAction<SetCreativeFlyCommand>(records[1]),
            DeserializeAction<SetCreativeFlyCommand>(records[2]),
            DeserializeAction<SetCreativeFlyCommand>(records[3]),
            DeserializeAction<RunCreativeFlyRevisionBurstCommand>(records[4]),
            DeserializeAction<SetCreativeFlyCommand>(records[5]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<CreativeFlyMultiplayerScenarioResult> RunAsync(
        CreativeFlyMultiplayerScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory, options.Transport);
        ValidateReplayPlan(options.ReplayPlan);
        NetworkFaultProfile faultProfile = (options.NetworkFaults
            ?? CreateDefaultNetworkFaults(options.Seed)).Validate();
        ValidateRequiredFaultProfile(faultProfile);

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(10);
        if (stepTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.StepTimeout));
        }
        if (scenarioTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.ScenarioTimeout));
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
            NodeRoles,
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
        int initialActorPlayerIndex = -1;

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
                ClientCount: 3,
                StartupTimeout: stepTimeout));
            RequiredAutomationCapabilities requiredCapabilities = new(
                SnapshotSchemaVersion: AutomationProtocol.CurrentSnapshotSchemaVersion,
                Commands: [
                    ControlCommandNames.Describe,
                    ControlCommandNames.GetSnapshotV2,
                    ControlCommandNames.GetEvents,
                    ControlCommandNames.ResetEvents,
                    ControlCommandNames.SetCreativeFly,
                    ControlCommandNames.RunCreativeFlyRevisionBurst,
                    ControlCommandNames.SetEnvironmentFixture,
                    ControlCommandNames.SetNetworkFaults,
                    ControlCommandNames.SaveWorld,
                    ControlCommandNames.ReloadWorld,
                    ControlCommandNames.Disconnect
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.Player,
                    AutomationCommandDomain.State
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"CreativeFly-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            initialActorPlayerIndex = actorPlayerIndex;
            GameNodeProcess actorClient = cluster.Clients[0];
            GameNodeProcess observerClient = cluster.Clients[1];
            GameNodeProcess lateClient = cluster.Clients[2];
            GameNodeProcess[] initialNodes = [cluster.Server, actorClient, observerClient];
            metrics["actor.initialPlayerIndex"] = actorPlayerIndex.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(cluster.RoomCode)) {
                metrics["relay.initialRoomCode"] = cluster.RoomCode;
            }

            await VerifyAuthorityRejectionsAsync(
                cluster,
                runner,
                actorPlayerIndex,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-command-authority",
                "passed",
                [observerClient.Role],
                "a client command targeting another player's creative-fly state was rejected with permission_denied"));

            await runner.RunAsync(
                "reset-events",
                ControlCommandNames.ResetEvents,
                token => Task.WhenAll(initialNodes.Select(node =>
                    node.SendAsync<EmptyCommand, ResetEventsResult>(
                        ControlCommandNames.ResetEvents,
                        new EmptyCommand(),
                        cancellationToken: token))),
                scenarioToken).ConfigureAwait(false);

            CreativeFlyReplayPlan plan = options.ReplayPlan
                ?? CreateDefaultReplayPlan(actorPlayerIndex);
            ActionReceipt hostReceipt = await RunSetActionAsync(
                "host-enable",
                cluster.Server,
                initialNodes,
                plan.HostEnable with { PlayerIndex = null },
                expectedEnabled: true,
                runner,
                snapshots,
                events,
                actions,
                assertions,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            metrics["host.playerIndex"] = hostReceipt.ActorPlayerIndex
                .ToString(CultureInfo.InvariantCulture);

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            _ = await runner.RunAsync(
                "set-server-survival",
                ControlCommandNames.SetEnvironmentFixture,
                token => cluster.Server.SendAsync<
                    SetEnvironmentFixtureCommand,
                    SetEnvironmentFixtureResult>(
                    ControlCommandNames.SetEnvironmentFixture,
                    new SetEnvironmentFixtureCommand(CreativeMode: false),
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);

            await RunSetActionAsync(
                "authority-rejection",
                actorClient,
                initialNodes,
                plan.AuthorityRejection with { PlayerIndex = actorPlayerIndex },
                expectedEnabled: false,
                runner,
                snapshots,
                events,
                actions,
                assertions,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            _ = await runner.RunAsync(
                "restore-server-creative",
                ControlCommandNames.SetEnvironmentFixture,
                token => cluster.Server.SendAsync<
                    SetEnvironmentFixtureCommand,
                    SetEnvironmentFixtureResult>(
                    ControlCommandNames.SetEnvironmentFixture,
                    new SetEnvironmentFixtureCommand(CreativeMode: true),
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);

            await RunSetActionAsync(
                "enable",
                actorClient,
                initialNodes,
                plan.Enable with { PlayerIndex = actorPlayerIndex },
                expectedEnabled: true,
                runner,
                snapshots,
                events,
                actions,
                assertions,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            await RunSetActionAsync(
                "disable",
                actorClient,
                initialNodes,
                plan.Disable with { PlayerIndex = actorPlayerIndex },
                expectedEnabled: false,
                runner,
                snapshots,
                events,
                actions,
                assertions,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            RunCreativeFlyRevisionBurstCommand burstCommand = plan.RevisionBurst with {
                PlayerIndex = actorPlayerIndex
            };
            CreativeFlyRevisionBurstResult burst = await runner.RunAsync(
                "execute-revision-burst",
                ControlCommandNames.RunCreativeFlyRevisionBurst,
                token => cluster.Server.SendAsync<
                    RunCreativeFlyRevisionBurstCommand,
                    CreativeFlyRevisionBurstResult>(
                    ControlCommandNames.RunCreativeFlyRevisionBurst,
                    burstCommand,
                    cancellationToken: token),
                result => $"revision={result.IntermediateRevision}->{result.FinalRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(burst.Receipt);
            if (burst.IntermediateRevision >= burst.FinalRevision) {
                throw new InvalidOperationException(
                    "Creative-fly revision burst did not advance the authoritative revision.");
            }
            actions.Add(CreateAction(cluster.Server, ControlCommandNames.RunCreativeFlyRevisionBurst, burstCommand, burst.Receipt));
            events["server-revision-burst"] = await runner.RunAsync(
                "verify-revision-burst-events",
                ControlCommandNames.GetEvents,
                token => WaitForAppliedEventAsync(
                    cluster.Server,
                    burst.Receipt.ActionId,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            Dictionary<string, StateSnapshotV2> revisionSnapshots = await runner.RunAsync(
                "verify-revision-burst-state",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForCreativeFlyOnNodesAsync(
                    initialNodes,
                    actorPlayerIndex,
                    expected: false,
                    requireIgnoredStaleOnClients: true,
                    requireFaultDrain: true,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, revisionSnapshots, "revision-burst");
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-revision-ordering",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"playerIndex={actorPlayerIndex}; final=false; revision="
                + $"{burst.IntermediateRevision}->{burst.FinalRevision}; stale ignored by clients"));

            long duplicatedPackets = await runner.RunAsync(
                "verify-network-faults",
                ControlCommandNames.GetSnapshotV2,
                _ => Task.FromResult(VerifyNetworkFaultEvidence(revisionSnapshots)),
                count => $"duplicated={count}",
                scenarioToken).ConfigureAwait(false);
            metrics["fault.duplicatedPackets"] = duplicatedPackets.ToString(CultureInfo.InvariantCulture);
            metrics["fault.profile"] = FormatFaultProfile(faultProfile);
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-under-network-faults",
                "passed",
                [cluster.Server.Role, actorClient.Role],
                $"duplicate/reorder converged; duplicated={duplicatedPackets}; {FormatFaultProfile(faultProfile)}"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(Seed: unchecked(faultProfile.Seed + 100)),
                    token),
                scenarioToken).ConfigureAwait(false);

            await RunSetActionAsync(
                "persistence-enable",
                actorClient,
                initialNodes,
                plan.PersistenceEnable with { PlayerIndex = actorPlayerIndex },
                expectedEnabled: true,
                runner,
                snapshots,
                events,
                actions,
                assertions,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "connect-late-client",
                "network.connect(client-c)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        lateClient,
                        "LearnerC",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 3,
                        stepTimeout,
                        "Server did not expose the late-joined client session.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            snapshots["client-c-after-late-join"] = await runner.RunAsync(
                "verify-late-join-state",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForCreativeFlyAsync(
                    lateClient,
                    actorPlayerIndex,
                    expected: true,
                    requireIgnoredStale: false,
                    requireFaultDrain: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-late-join-baseline",
                "passed",
                [lateClient.Role],
                $"playerIndex={actorPlayerIndex}; enabled=true"));

            GameNodeProcess[] allActiveNodes = [cluster.Server, actorClient, observerClient, lateClient];
            await runner.RunAsync(
                "disconnect-actor",
                ControlCommandNames.Disconnect,
                async token => {
                    _ = await actorClient.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await actorClient.WaitForStatusAsync(
                        status => !status.ProjectLoaded
                            && !status.IsClientRunning
                            && string.Equals(status.Screen, "MainMenuScreen", StringComparison.Ordinal),
                        stepTimeout,
                        "Client A did not return to MainMenu after disconnect.",
                        token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 2,
                        stepTimeout,
                        "Server retained the disconnected actor session.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);

            NodeStatus reconnectedActor = await runner.RunAsync(
                "reconnect-actor",
                "network.connect(client-a)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        actorClient,
                        "LearnerA",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 3,
                        stepTimeout,
                        "Server did not restore the actor session.",
                        token).ConfigureAwait(false);
                    return await actorClient.GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"playerIndex={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            EnsureActorIdentity(reconnectedActor, initialActorPlayerIndex, "reconnect");
            actorPlayerIndex = reconnectedActor.PlayerIndex;
            Dictionary<string, StateSnapshotV2> reconnectSnapshots = await runner.RunAsync(
                "verify-reconnect-state",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForCreativeFlyOnNodesAsync(
                    allActiveNodes,
                    actorPlayerIndex,
                    expected: true,
                    requireIgnoredStaleOnClients: false,
                    requireFaultDrain: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, reconnectSnapshots, "after-reconnect");
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-reconnect-persistence",
                "passed",
                allActiveNodes.Select(node => node.Role).ToArray(),
                $"playerIndex={actorPlayerIndex}; identity preserved; enabled=true"));

            string savedWorldDirectory = await runner.RunAsync(
                "save-and-stop-world",
                ControlCommandNames.SaveWorld,
                async token => {
                    SaveWorldResult saved = await cluster.Server.SendAsync<SaveWorldCommand, SaveWorldResult>(
                        ControlCommandNames.SaveWorld,
                        new SaveWorldCommand(),
                        timeout: stepTimeout,
                        cancellationToken: token).ConfigureAwait(false);
                    if (!saved.Saved || string.IsNullOrWhiteSpace(saved.DirectoryName)) {
                        throw new InvalidOperationException("Server declined to save the creative-fly world.");
                    }
                    await Task.WhenAll(cluster.Clients.Select(client =>
                        client.SendAsync<DisconnectCommand, ControlAck>(
                            ControlCommandNames.Disconnect,
                            new DisconnectCommand(),
                            cancellationToken: token))).ConfigureAwait(false);
                    await Task.WhenAll(cluster.Clients.Select(client =>
                        client.WaitForStatusAsync(
                            status => !status.ProjectLoaded
                                && !status.IsClientRunning
                                && string.Equals(status.Screen, "MainMenuScreen", StringComparison.Ordinal),
                            stepTimeout,
                            $"Node '{client.Role}' did not return to MainMenu before reload.",
                            token))).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 0,
                        stepTimeout,
                        "Server retained client sessions before persistence reload.",
                        token).ConfigureAwait(false);
                    _ = await cluster.Server.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(SaveWorld: true),
                        timeout: stepTimeout,
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => !status.ProjectLoaded
                            && !status.IsServerRunning
                            && string.Equals(status.Screen, "MainMenuScreen", StringComparison.Ordinal),
                        stepTimeout,
                        "Server did not return to MainMenu after saving.",
                        token).ConfigureAwait(false);
                    return saved.DirectoryName;
                },
                directory => directory,
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "reload-saved-world",
                ControlCommandNames.ReloadWorld,
                async token => {
                    _ = await cluster.Server.SendAsync<ReloadWorldCommand, ControlAck>(
                        ControlCommandNames.ReloadWorld,
                        new ReloadWorldCommand(savedWorldDirectory),
                        cancellationToken: token).ConfigureAwait(false);
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
                        "Saved creative-fly world did not become playable.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            string? reloadRoomCode = await runner.RunAsync(
                "restart-transport-host",
                "network.startHost",
                token => cluster.StartHostAsync(stepTimeout, token),
                room => room ?? "direct",
                scenarioToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(reloadRoomCode)) {
                metrics["relay.reloadRoomCode"] = reloadRoomCode;
            }

            NodeStatus persistedActor = await runner.RunAsync(
                "reconnect-persisted-actor",
                "network.connect(client-a)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        actorClient,
                        "LearnerA",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 1,
                        stepTimeout,
                        "Server did not expose the persisted actor session.",
                        token).ConfigureAwait(false);
                    return await actorClient.GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"playerIndex={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            EnsureActorIdentity(persistedActor, initialActorPlayerIndex, "save/reload");
            actorPlayerIndex = persistedActor.PlayerIndex;
            GameNodeProcess[] serverPersistenceNodes = [cluster.Server, actorClient];
            Dictionary<string, StateSnapshotV2> serverPersistenceSnapshots = await runner.RunAsync(
                "verify-server-persistence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForCreativeFlyOnNodesAsync(
                    serverPersistenceNodes,
                    actorPlayerIndex,
                    expected: true,
                    requireIgnoredStaleOnClients: false,
                    requireFaultDrain: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, serverPersistenceSnapshots, "after-reload");

            await runner.RunAsync(
                "connect-persistence-observer",
                "network.connect(client-c)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        lateClient,
                        "LearnerC",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 2,
                        stepTimeout,
                        "Server did not expose the persistence observer session.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            GameNodeProcess[] persistedObservationNodes = [cluster.Server, actorClient, lateClient];
            Dictionary<string, StateSnapshotV2> clientPersistenceSnapshots = await runner.RunAsync(
                "verify-client-persistence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForCreativeFlyOnNodesAsync(
                    persistedObservationNodes,
                    actorPlayerIndex,
                    expected: true,
                    requireIgnoredStaleOnClients: false,
                    requireFaultDrain: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, clientPersistenceSnapshots, "persistence-observer");
            assertions.Add(new ScenarioAssertionRecordV2(
                "creative-fly-save-reload-persistence",
                "passed",
                persistedObservationNodes.Select(node => node.Role).ToArray(),
                $"world={savedWorldDirectory}; playerIndex={actorPlayerIndex}; enabled=true"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(cluster, snapshots, events).ConfigureAwait(false);
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
            metrics["actorPlayerIndex"] = actorPlayerIndex.ToString(CultureInfo.InvariantCulture);
            metrics["transport"] = options.Transport.ToString();
            metrics["actionCount"] = actions.Count.ToString(CultureInfo.InvariantCulture);
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
                    faultProfile);
                ScenarioTopologyV2 topology = new(
                    options.Transport.ToString(),
                    "server",
                    ["client-a", "client-b", "client-c"],
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
            actions.Count,
            stopwatch.Elapsed);
    }

    private static async Task<ActionReceipt> RunSetActionAsync(
        string label,
        GameNodeProcess actor,
        IReadOnlyCollection<GameNodeProcess> nodes,
        SetCreativeFlyCommand command,
        bool expectedEnabled,
        ScenarioRunner runner,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events,
        List<ScenarioActionRecordV2> actions,
        List<ScenarioAssertionRecordV2> assertions,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        ActionReceipt receipt = await runner.RunAsync(
            $"execute-{label}",
            ControlCommandNames.SetCreativeFly,
            token => actor.SendAsync<SetCreativeFlyCommand, ActionReceipt>(
                ControlCommandNames.SetCreativeFly,
                command,
                cancellationToken: token),
            value => $"actor={value.ActorPlayerIndex}; code={value.Code}",
            cancellationToken).ConfigureAwait(false);
        EnsureAccepted(receipt);
        ActionProvenance expectedProvenance = command.ExpectedResult == CreativeFlyExpectedResult.Accepted
            ? ActionProvenance.ProductionInput
            : ActionProvenance.FaultInjection;
        if (receipt.Provenance != expectedProvenance
            || !string.Equals(
                receipt.ProductionEntry,
                ProductionPathEntries.CreativeFly,
                StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Creative-fly action '{label}' did not attest the expected production path.");
        }
        actions.Add(CreateAction(actor, ControlCommandNames.SetCreativeFly, command, receipt));
        ActionEventsResult actionEvents = await runner.RunAsync(
            $"verify-{label}-events",
            ControlCommandNames.GetEvents,
            token => WaitForAppliedEventAsync(actor, receipt.ActionId, timeout, token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        EnsureCreativeFlyActionEvents(actionEvents, receipt.ActionId, command.ExpectedResult);
        events[$"{actor.Role}-{label}"] = actionEvents;
        Dictionary<string, StateSnapshotV2> converged = await runner.RunAsync(
            $"verify-{label}-state",
            ControlCommandNames.GetSnapshotV2,
            token => WaitForCreativeFlyOnNodesAsync(
                nodes,
                command.PlayerIndex ?? receipt.ActorPlayerIndex,
                expectedEnabled,
                requireIgnoredStaleOnClients: false,
                requireFaultDrain: false,
                timeout,
                token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AddSnapshots(snapshots, converged, label);
        assertions.Add(new ScenarioAssertionRecordV2(
            $"creative-fly-{label}",
            "passed",
            nodes.Select(node => node.Role).ToArray(),
            $"playerIndex={receipt.ActorPlayerIndex}; enabled={expectedEnabled}; "
            + $"result={command.ExpectedResult}; duplicates={command.DuplicateCount}"));
        return receipt;
    }

    private static ScenarioActionRecordV2 CreateAction<T>(
        GameNodeProcess node,
        string command,
        T payload,
        ActionReceipt receipt) =>
        new(
            node.Role,
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            receipt);

    private static async Task<ActionEventsResult> WaitForAppliedEventAsync(
        GameNodeProcess node,
        string actionId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        ActionEventsResult? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                ControlCommandNames.GetEvents,
                new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            ActionEventRecord[] matching = last.Events
                .Where(item => string.Equals(item.ActionId, actionId, StringComparison.Ordinal))
                .ToArray();
            ActionEventRecord? rejected = matching.LastOrDefault(item => item.Phase == ActionEventPhase.Rejected);
            if (rejected != null) {
                throw new InvalidOperationException(
                    $"Action '{actionId}' was rejected by the runtime with code '{rejected.Code}'.");
            }
            if (matching.Any(item => item.Phase == ActionEventPhase.Applied)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        string phases = last == null
            ? "missing"
            : string.Join(',', last.Events
                .Where(item => string.Equals(item.ActionId, actionId, StringComparison.Ordinal))
                .Select(item => item.Phase));
        throw new TimeoutException(
            $"Action '{actionId}' did not publish an applied event. Phases={phases}.");
    }

    private static void EnsureCreativeFlyActionEvents(
        ActionEventsResult result,
        string actionId,
        CreativeFlyExpectedResult expectedResult) {
        ActionEventRecord[] matching = result.Events
            .Where(item => string.Equals(item.ActionId, actionId, StringComparison.Ordinal))
            .ToArray();
        ActionEventRecord? queued = matching.FirstOrDefault(item => item.Phase == ActionEventPhase.Queued);
        ActionEventRecord? applied = matching.LastOrDefault(item => item.Phase == ActionEventPhase.Applied);
        if (queued == null
            || applied == null
            || !applied.Data.TryGetValue("result", out string? actualResult)
            || !string.Equals(actualResult, expectedResult.ToString(), StringComparison.Ordinal)
            || !applied.Data.ContainsKey("networkRequestId")) {
            throw new InvalidOperationException(
                $"Action '{actionId}' lacks a queued/applied authoritative creative-fly result.");
        }
    }

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForCreativeFlyOnNodesAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        int playerIndex,
        bool expected,
        bool requireIgnoredStaleOnClients,
        bool requireFaultDrain,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        GameNodeProcess[] ordered = nodes.ToArray();
        StateSnapshotV2[] captured = await Task.WhenAll(ordered.Select(node =>
            WaitForCreativeFlyAsync(
                node,
                playerIndex,
                expected,
                requireIgnoredStaleOnClients && !string.Equals(node.Role, "server", StringComparison.Ordinal),
                requireFaultDrain,
                timeout,
                cancellationToken))).ConfigureAwait(false);
        return ordered
            .Select((node, index) => (node.Role, Snapshot: captured[index]))
            .ToDictionary(item => item.Role, item => item.Snapshot, StringComparer.Ordinal);
    }

    private static async Task<StateSnapshotV2> WaitForCreativeFlyAsync(
        GameNodeProcess node,
        int playerIndex,
        bool expected,
        bool requireIgnoredStale,
        bool requireFaultDrain,
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
            bool staleSatisfied = !requireIgnoredStale
                || last.StateRevisions?.IgnoredStaleStates > 0;
            bool faultDrainSatisfied = !requireFaultDrain
                || last.Network?.FaultPending == 0;
            if (player?.ReadyForPlaying == true
                && player.CreativeFlyEnabled == expected
                && staleSatisfied
                && faultDrainSatisfied) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        PlayerSnapshotV2? lastPlayer = last?.Players.SingleOrDefault(item => item.PlayerIndex == playerIndex);
        throw new TimeoutException(
            $"Node '{node.Role}' player {playerIndex} did not converge to creativeFly={expected}; "
            + $"player={lastPlayer}; ignoredStale={last?.StateRevisions?.IgnoredStaleStates}; "
            + $"faultPending={last?.Network?.FaultPending}.");
    }

    private static async Task ConfigureNetworkFaultsAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        NetworkFaultProfile baseProfile,
        CancellationToken cancellationToken) {
        GameNodeProcess[] ordered = nodes.ToArray();
        NetworkFaultProfile[] expected = ordered
            .Select((_, index) => baseProfile with { Seed = unchecked(baseProfile.Seed + index) })
            .ToArray();
        NetworkFaultState[] actual = await Task.WhenAll(ordered.Select((node, index) =>
            node.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(expected[index]),
                cancellationToken: cancellationToken))).ConfigureAwait(false);
        for (int index = 0; index < actual.Length; index++) {
            VerifyFaultProfile(actual[index].Profile, expected[index], ordered[index].Role);
        }
    }

    private static void VerifyFaultProfile(
        NetworkFaultProfile actual,
        NetworkFaultProfile expected,
        string role) {
        if (actual with { TargetPacketTypes = null }
                != expected with { TargetPacketTypes = null }
            || !(actual.TargetPacketTypes ?? []).SequenceEqual(
                expected.TargetPacketTypes ?? [],
                StringComparer.Ordinal)) {
            throw new InvalidOperationException(
                $"Node '{role}' did not apply its deterministic targeted fault profile.");
        }
    }

    private static long VerifyNetworkFaultEvidence(
        IReadOnlyDictionary<string, StateSnapshotV2> snapshots) {
        long serverDuplicates = RequireNetwork(snapshots, "server").FaultDuplicated;
        long actorDuplicates = RequireNetwork(snapshots, "client-a").FaultDuplicated;
        if (serverDuplicates <= 0 || actorDuplicates <= 0) {
            throw new InvalidOperationException(
                $"Targeted creative-fly faults did not duplicate traffic on both directions. "
                + $"server={serverDuplicates}; client-a={actorDuplicates}.");
        }
        if (snapshots.Values.Any(snapshot => snapshot.Network?.FaultPending != 0)) {
            throw new InvalidOperationException("Creative-fly fault queues did not drain.");
        }
        return snapshots.Values.Sum(snapshot => snapshot.Network?.FaultDuplicated ?? 0);
    }

    private static NetworkSnapshotV2 RequireNetwork(
        IReadOnlyDictionary<string, StateSnapshotV2> snapshots,
        string role) =>
        snapshots.TryGetValue(role, out StateSnapshotV2? snapshot)
            ? snapshot.Network
                ?? throw new InvalidOperationException($"Snapshot '{role}' lacks network state.")
            : throw new InvalidOperationException($"Snapshot '{role}' is missing.");

    private static async Task VerifyAuthorityRejectionsAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int actorPlayerIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        SetCreativeFlyCommand command = new(true, actorPlayerIndex);
        await runner.RunAsync(
            "reject-server-originated-action",
            ControlCommandNames.SetCreativeFly,
            async token => {
                ControlCommandException exception = await AssertCommandRejectedAsync(() =>
                    cluster.Server.SendAsync<SetCreativeFlyCommand, ActionReceipt>(
                        ControlCommandNames.SetCreativeFly,
                        command,
                        cancellationToken: token)).ConfigureAwait(false);
                EnsureRejectionCode(exception, "permission_denied");
            },
            cancellationToken).ConfigureAwait(false);
        await runner.RunAsync(
            "reject-non-owner-action",
            ControlCommandNames.SetCreativeFly,
            async token => {
                _ = await WaitForCreativeFlyAsync(
                    cluster.Clients[1],
                    actorPlayerIndex,
                    expected: false,
                    requireIgnoredStale: false,
                    requireFaultDrain: false,
                    timeout,
                    token).ConfigureAwait(false);
                ControlCommandException exception = await AssertCommandRejectedAsync(() =>
                    cluster.Clients[1].SendAsync<SetCreativeFlyCommand, ActionReceipt>(
                        ControlCommandNames.SetCreativeFly,
                        command,
                        cancellationToken: token)).ConfigureAwait(false);
                EnsureRejectionCode(exception, "permission_denied");
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ControlCommandException> AssertCommandRejectedAsync<T>(
        Func<Task<T>> action) {
        try {
            _ = await action().ConfigureAwait(false);
        }
        catch (ControlCommandException exception) {
            return exception;
        }
        throw new InvalidOperationException("Expected the automation command to be rejected.");
    }

    private static void EnsureRejectionCode(
        ControlCommandException exception,
        string expectedCode) {
        if (!string.Equals(exception.Code, expectedCode, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Expected {expectedCode}, got {exception.Code}.",
                exception);
        }
    }

    private static void EnsureAccepted(ActionReceipt receipt) {
        if (!receipt.Accepted) {
            throw new InvalidOperationException(
                $"Action '{receipt.ActionId}' was rejected with code '{receipt.Code}': {receipt.Detail}");
        }
    }

    private static void EnsureActorIdentity(
        NodeStatus status,
        int expectedPlayerIndex,
        string phase) {
        if (!status.MainPlayerReady || status.PlayerIndex != expectedPlayerIndex) {
            throw new InvalidOperationException(
                $"Actor identity changed during {phase}. Expected playerIndex={expectedPlayerIndex}; "
                + $"actual={status.PlayerIndex}; ready={status.MainPlayerReady}.");
        }
    }

    private static CreativeFlyReplayPlan CreateDefaultReplayPlan(int playerIndex) =>
        new(
            new SetCreativeFlyCommand(true),
            new SetCreativeFlyCommand(
                true,
                playerIndex,
                CreativeFlyExpectedResult.InvalidGameMode),
            new SetCreativeFlyCommand(
                true,
                playerIndex,
                CreativeFlyExpectedResult.Accepted,
                DuplicateCount: 3),
            new SetCreativeFlyCommand(
                false,
                playerIndex,
                CreativeFlyExpectedResult.Accepted,
                DuplicateCount: 3),
            new RunCreativeFlyRevisionBurstCommand(
                playerIndex,
                IntermediateEnabled: true,
                FinalEnabled: false,
                SendStaleState: true),
            new SetCreativeFlyCommand(
                true,
                playerIndex,
                CreativeFlyExpectedResult.Accepted));

    private static void ValidateReplayPlan(CreativeFlyReplayPlan? plan) {
        if (plan == null) {
            return;
        }
        ValidateSetAction(
            plan.HostEnable,
            enabled: true,
            CreativeFlyExpectedResult.Accepted,
            "hostEnable");
        if (plan.HostEnable.DuplicateCount != 1) {
            throw new InvalidDataException(
                "Creative-fly replay host action requires duplicateCount=1.");
        }
        ValidateSetAction(
            plan.AuthorityRejection,
            enabled: true,
            CreativeFlyExpectedResult.InvalidGameMode,
            "authorityRejection");
        ValidateSetAction(plan.Enable, enabled: true, CreativeFlyExpectedResult.Accepted, "enable");
        ValidateSetAction(plan.Disable, enabled: false, CreativeFlyExpectedResult.Accepted, "disable");
        ValidateSetAction(
            plan.PersistenceEnable,
            enabled: true,
            CreativeFlyExpectedResult.Accepted,
            "persistenceEnable");
        if (!plan.RevisionBurst.IntermediateEnabled
            || plan.RevisionBurst.FinalEnabled
            || !plan.RevisionBurst.SendStaleState) {
            throw new InvalidDataException(
                "Creative-fly replay requires an enabled intermediate state, disabled final state and stale-state delivery.");
        }
    }

    private static void ValidateSetAction(
        SetCreativeFlyCommand command,
        bool enabled,
        CreativeFlyExpectedResult expectedResult,
        string name) {
        if (command.Enabled != enabled
            || command.ExpectedResult != expectedResult
            || command.DuplicateCount is < 1 or > 16) {
            throw new InvalidDataException(
                $"Creative-fly replay action '{name}' has incompatible state, result or duplicate count.");
        }
    }

    private static void ValidateRequiredFaultProfile(NetworkFaultProfile profile) {
        string[] targets = profile.TargetPacketTypes ?? [];
        if (profile.DuplicationRate <= 0
            || profile.ReorderRate <= 0
            || !targets.SequenceEqual([FaultPacketType], StringComparer.Ordinal)) {
            throw new ArgumentException(
                "Creative-fly resilience requires duplicate/reorder faults targeted to PlayerCreativeFlyPacket.");
        }
    }

    private static T DeserializeAction<T>(ScenarioActionRecordV2 record) =>
        record.Payload.Deserialize<T>(ControlProtocol.JsonOptions)
        ?? throw new InvalidDataException(
            $"Replay action '{record.Receipt.ActionId}' has an invalid {typeof(T).Name} payload.");

    private static void AddSnapshots(
        IDictionary<string, StateSnapshotV2> target,
        IReadOnlyDictionary<string, StateSnapshotV2> source,
        string suffix) {
        foreach ((string role, StateSnapshotV2 snapshot) in source) {
            target[$"{role}-{suffix}"] = snapshot;
        }
    }

    private static string FormatFaultProfile(NetworkFaultProfile profile) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"seed={profile.Seed}; latencyMs={profile.BaseLatencyMilliseconds}; "
            + $"jitterMs={profile.JitterMilliseconds}; duplicate={profile.DuplicationRate:R}; "
            + $"reorder={profile.ReorderRate:R}; reorderDelayMs={profile.ReorderDelayMilliseconds}; "
            + $"targets={string.Join(',', profile.TargetPacketTypes ?? [])}");

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            try {
                GetSnapshotV2Command command = new([
                        SnapshotDomain.Network,
                        SnapshotDomain.Players,
                        SnapshotDomain.Events
                    ], EventLimit: AutomationProtocol.MaximumEventQueryCount);
                snapshots[$"{node.Role}-final"] =
                    await MultiplayerScenarioOperations
                        .CaptureAfterApplicationReliabilityIdleAsync(
                            node,
                            token => node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                                ControlCommandNames.GetSnapshotV2,
                                command,
                                timeout: TimeSpan.FromSeconds(5),
                                cancellationToken: token),
                            TimeSpan.FromSeconds(10),
                            CancellationToken.None).ConfigureAwait(false);
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
                // A dead or wedged node can still contribute retained process logs.
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
