using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record PlayerMotionReplayPlan(
    PlayerMoveCommand InitialMove,
    PlayerLookCommand InitialLook,
    PlayerSetCrouchCommand InitialCrouch,
    PlayerLookCommand PersistenceLook,
    PlayerSetCrouchCommand PersistenceCrouch);

public sealed record PlayerMotionMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    PlayerMotionReplayPlan? ReplayPlan = null);

public sealed record PlayerMotionMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class PlayerMotionMultiplayerScenarioException : Exception {
    public PlayerMotionMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class PlayerMotionMultiplayerScenario {
    public const string ScenarioName = "player-motion-multiplayer";
    public const int ScenarioVersion = 1;

    private const double PositionConvergenceTolerance = 0.4;
    private const double PersistencePositionTolerance = 0.6;
    private const double LookConvergenceTolerance = 0.04;
    private const double RotationConvergenceTolerance = 0.05;
    private const double SettledVelocity = 0.3;

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
        new("reject-server-originated-action", ControlCommandNames.PlayerMove),
        new("reject-non-owner-action", ControlCommandNames.PlayerMove),
        new("reset-events", ControlCommandNames.ResetEvents),
        new("prepare-motion-fixture", ControlCommandNames.PreparePlayerMotionFixture),
        new("apply-motion-fixture", ControlCommandNames.ApplyPlayerMotionFixture),
        new("verify-motion-baseline", ControlCommandNames.GetSnapshotV2),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("execute-move", ControlCommandNames.PlayerMove),
        new("verify-move-events", ControlCommandNames.GetEvents),
        new("verify-move-convergence", ControlCommandNames.GetSnapshotV2),
        new("execute-look", ControlCommandNames.PlayerLook),
        new("verify-look-events", ControlCommandNames.GetEvents),
        new("verify-look-convergence", ControlCommandNames.GetSnapshotV2),
        new("execute-crouch", ControlCommandNames.PlayerSetCrouch),
        new("verify-crouch-events", ControlCommandNames.GetEvents),
        new("verify-crouch-convergence", ControlCommandNames.GetSnapshotV2),
        new("verify-network-faults", ControlCommandNames.GetSnapshotV2),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("connect-late-client", "network.connect(client-c)"),
        new("verify-late-join-state", ControlCommandNames.GetSnapshotV2),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("reconnect-actor", "network.connect(client-a)"),
        new("verify-reconnect-semantics", ControlCommandNames.GetSnapshotV2),
        new("prepare-persistence-fixture", ControlCommandNames.PreparePlayerMotionFixture),
        new("apply-persistence-fixture", ControlCommandNames.ApplyPlayerMotionFixture),
        new("verify-persistence-baseline", ControlCommandNames.GetSnapshotV2),
        new("execute-persistence-look", ControlCommandNames.PlayerLook),
        new("verify-persistence-look-events", ControlCommandNames.GetEvents),
        new("verify-persistence-look-convergence", ControlCommandNames.GetSnapshotV2),
        new("execute-persistence-crouch", ControlCommandNames.PlayerSetCrouch),
        new("verify-persistence-crouch-events", ControlCommandNames.GetEvents),
        new("verify-persistence-crouch-convergence", ControlCommandNames.GetSnapshotV2),
        new("capture-pre-save-state", ControlCommandNames.GetSnapshotV2),
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
            Seed: unchecked(seed + 503),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18);

    public static PlayerMotionReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        string[] expectedCommands = [
            ControlCommandNames.PlayerMove,
            ControlCommandNames.PlayerLook,
            ControlCommandNames.PlayerSetCrouch,
            ControlCommandNames.PlayerLook,
            ControlCommandNames.PlayerSetCrouch
        ];
        if (records.Count != expectedCommands.Length) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly "
                + $"{expectedCommands.Length} recorded actions.");
        }
        for (int index = 0; index < records.Count; index++) {
            ScenarioActionRecordV2 record = records[index];
            if (!string.Equals(record.Role, "client-a", StringComparison.Ordinal)) {
                throw new InvalidDataException(
                    $"Replay action {index} has unsupported role '{record.Role}'.");
            }
            if (!string.Equals(record.Command, expectedCommands[index], StringComparison.Ordinal)) {
                throw new InvalidDataException(
                    $"Replay action {index} must be '{expectedCommands[index]}', "
                    + $"got '{record.Command}'.");
            }
        }
        PlayerMotionReplayPlan replay = new(
            DeserializeAction<PlayerMoveCommand>(records[0]),
            DeserializeAction<PlayerLookCommand>(records[1]),
            DeserializeAction<PlayerSetCrouchCommand>(records[2]),
            DeserializeAction<PlayerLookCommand>(records[3]),
            DeserializeAction<PlayerSetCrouchCommand>(records[4]));
        ValidateReplayPlan(replay);
        return replay;
    }

    public async Task<PlayerMotionMultiplayerScenarioResult> RunAsync(
        PlayerMotionMultiplayerScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory, options.Transport);
        ValidateReplayPlan(options.ReplayPlan);
        NetworkFaultProfile faultProfile = (options.NetworkFaults
            ?? CreateDefaultNetworkFaults(options.Seed)).Validate();
        if (!IsFaultProfileEnabled(faultProfile)) {
            throw new ArgumentException(
                "Player motion resilience requires a non-empty network fault profile.",
                nameof(options.NetworkFaults));
        }

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(9);
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
                    ControlCommandNames.Disconnect,
                    ControlCommandNames.SaveWorld,
                    ControlCommandNames.ReloadWorld,
                    ControlCommandNames.SetNetworkFaults,
                    ControlCommandNames.PreparePlayerMotionFixture,
                    ControlCommandNames.ApplyPlayerMotionFixture,
                    ControlCommandNames.PlayerMove,
                    ControlCommandNames.PlayerLook,
                    ControlCommandNames.PlayerSetCrouch
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Player
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"PlayerMotion-{options.Transport}-{options.Seed}",
                options.Seed,
                2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            int initialActorPlayerIndex = actorPlayerIndex;
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

            await runner.RunAsync(
                "reset-events",
                ControlCommandNames.ResetEvents,
                token => Task.WhenAll(MultiplayerGameplayBootstrap.AllNodes(cluster).Select(node =>
                    node.SendAsync<EmptyCommand, ResetEventsResult>(
                        ControlCommandNames.ResetEvents,
                        new EmptyCommand(),
                        cancellationToken: token))),
                scenarioToken).ConfigureAwait(false);

            GameNodeProcess actorClient = cluster.Clients[0];
            GameNodeProcess[] initialNodes = [
                cluster.Server,
                actorClient,
                cluster.Clients[1]
            ];
            PlayerMotionFixture initialFixture = await PrepareAndApplyFixtureAsync(
                cluster,
                runner,
                actorPlayerIndex,
                startPosition: null,
                "motion",
                initialNodes,
                stepTimeout,
                snapshots,
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            PlayerMotionReplayPlan plan = options.ReplayPlan ?? CreateDefaultActionPlan(actorPlayerIndex);
            PlayerBodySnapshotV2 moved = await RunMotionActionAsync(
                actorClient,
                initialNodes,
                runner,
                actorPlayerIndex,
                new MotionCommandPlan("move", MotionEffectKind.Move, plan.InitialMove),
                stepTimeout,
                snapshots,
                events,
                actions,
                assertions,
                scenarioToken).ConfigureAwait(false);
            PlayerBodySnapshotV2 looked = await RunMotionActionAsync(
                actorClient,
                initialNodes,
                runner,
                actorPlayerIndex,
                new MotionCommandPlan("look", MotionEffectKind.Look, plan.InitialLook),
                stepTimeout,
                snapshots,
                events,
                actions,
                assertions,
                scenarioToken).ConfigureAwait(false);
            PlayerBodySnapshotV2 crouched = await RunMotionActionAsync(
                actorClient,
                initialNodes,
                runner,
                actorPlayerIndex,
                new MotionCommandPlan("crouch", MotionEffectKind.SetCrouch, plan.InitialCrouch),
                stepTimeout,
                snapshots,
                events,
                actions,
                assertions,
                scenarioToken).ConfigureAwait(false);
            metrics["motion.horizontalDistance"] = HorizontalDistance(
                    initialFixture.StartPosition,
                    moved.Position)
                .ToString("R", CultureInfo.InvariantCulture);
            metrics["look.delta"] = LookDistance(
                    moved.LookAngles ?? new Vector2State(0, 0),
                    looked.LookAngles ?? new Vector2State(0, 0))
                .ToString("R", CultureInfo.InvariantCulture);
            metrics["rotation.delta"] = RotationDistance(
                    moved.Rotation
                        ?? throw new InvalidOperationException("Move snapshot lacks rotation."),
                    looked.Rotation
                        ?? throw new InvalidOperationException("Look snapshot lacks rotation."))
                .ToString("R", CultureInfo.InvariantCulture);

            long duplicatedPackets = await runner.RunAsync(
                "verify-network-faults",
                ControlCommandNames.GetSnapshotV2,
                _ => Task.FromResult(VerifyNetworkFaultEvidence(snapshots, initialNodes, "crouch")),
                value => $"duplicated={value}",
                scenarioToken).ConfigureAwait(false);
            metrics["fault.duplicatedPackets"] = duplicatedPackets.ToString(CultureInfo.InvariantCulture);
            metrics["fault.profile"] = FormatFaultProfile(faultProfile);
            assertions.Add(new ScenarioAssertionRecordV2(
                "motion-action-under-network-faults",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"move/look/crouch converged; {FormatFaultProfile(faultProfile)}; "
                + $"duplicated={duplicatedPackets}"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(Seed: unchecked(faultProfile.Seed + 100)),
                    token),
                scenarioToken).ConfigureAwait(false);

            ExpectedActorState preDisconnectState = ExpectedActorState.From(crouched);
            GameNodeProcess lateClient = cluster.Clients[2];
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
                token => WaitForActorStateAsync(
                    lateClient,
                    actorPlayerIndex,
                    preDisconnectState,
                    PositionConvergenceTolerance,
                    LookConvergenceTolerance,
                    requireSettled: true,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "motion-late-join-convergence",
                "passed",
                [lateClient.Role],
                $"playerIndex={actorPlayerIndex}; position/look/crouch converged"));

            GameNodeProcess[] allNodes = MultiplayerGameplayBootstrap.AllNodes(cluster).ToArray();
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
            metrics["actor.reconnectedPlayerIndex"] = actorPlayerIndex.ToString(CultureInfo.InvariantCulture);

            ExpectedActorState reconnectExpected = new(
                preDisconnectState.Position,
                preDisconnectState.Rotation,
                new Vector2State(0, 0),
                false);
            Dictionary<string, StateSnapshotV2> reconnectSnapshots = await runner.RunAsync(
                "verify-reconnect-semantics",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForActorStateOnAllAsync(
                    allNodes,
                    actorPlayerIndex,
                    reconnectExpected,
                    PersistencePositionTolerance,
                    LookConvergenceTolerance,
                    requireSettled: true,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, reconnectSnapshots, "after-reconnect");
            assertions.Add(new ScenarioAssertionRecordV2(
                "motion-reconnect-semantics",
                "passed",
                allNodes.Select(node => node.Role).ToArray(),
                $"playerIndex={actorPlayerIndex}; position/rotation=persisted; "
                + "look=reset; crouch=reset; "
                + "uniquePlayerIndexes=true"));

            PlayerBodySnapshotV2 reconnectBody = RequireActorBody(
                reconnectSnapshots[actorClient.Role],
                actorPlayerIndex);
            _ = await PrepareAndApplyFixtureAsync(
                cluster,
                runner,
                actorPlayerIndex,
                reconnectBody.Position,
                "persistence",
                allNodes,
                stepTimeout,
                snapshots,
                scenarioToken).ConfigureAwait(false);

            _ = await RunMotionActionAsync(
                actorClient,
                allNodes,
                runner,
                actorPlayerIndex,
                new MotionCommandPlan(
                    "persistence-look",
                    MotionEffectKind.Look,
                    plan.PersistenceLook),
                stepTimeout,
                snapshots,
                events,
                actions,
                assertions,
                scenarioToken).ConfigureAwait(false);
            PlayerBodySnapshotV2 preSaveBody = await RunMotionActionAsync(
                actorClient,
                allNodes,
                runner,
                actorPlayerIndex,
                new MotionCommandPlan(
                    "persistence-crouch",
                    MotionEffectKind.SetCrouch,
                    plan.PersistenceCrouch),
                stepTimeout,
                snapshots,
                events,
                actions,
                assertions,
                scenarioToken).ConfigureAwait(false);
            ExpectedActorState preSaveState = ExpectedActorState.From(preSaveBody);

            snapshots["server-before-save"] = await runner.RunAsync(
                "capture-pre-save-state",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForActorStateAsync(
                    cluster.Server,
                    actorPlayerIndex,
                    preSaveState,
                    PositionConvergenceTolerance,
                    LookConvergenceTolerance,
                    requireSettled: true,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);

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
                        throw new InvalidOperationException(
                            "Server declined to save the player motion world.");
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
                        "Saved player motion world did not become playable.",
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

            ExpectedActorState persistedExpected = new(
                preSaveState.Position,
                preSaveState.Rotation,
                new Vector2State(0, 0),
                false);
            GameNodeProcess[] persistenceNodes = [cluster.Server, actorClient];
            Dictionary<string, StateSnapshotV2> serverPersistenceSnapshots = await runner.RunAsync(
                "verify-server-persistence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForActorStateOnAllAsync(
                    persistenceNodes,
                    actorPlayerIndex,
                    persistedExpected,
                    PersistencePositionTolerance,
                    LookConvergenceTolerance,
                    requireSettled: true,
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

            GameNodeProcess[] persistedObservationNodes = [
                cluster.Server,
                actorClient,
                lateClient
            ];
            Dictionary<string, StateSnapshotV2> clientPersistenceSnapshots = await runner.RunAsync(
                "verify-client-persistence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForActorStateOnAllAsync(
                    persistedObservationNodes,
                    actorPlayerIndex,
                    persistedExpected,
                    PersistencePositionTolerance,
                    LookConvergenceTolerance,
                    requireSettled: true,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, clientPersistenceSnapshots, "persistence-observer");
            assertions.Add(new ScenarioAssertionRecordV2(
                "motion-save-reload-semantics",
                "passed",
                persistedObservationNodes.Select(node => node.Role).ToArray(),
                $"world={savedWorldDirectory}; playerIndex={actorPlayerIndex}; "
                + "position/rotation=persisted; look=reset; crouch=reset"));
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
            throw new PlayerMotionMultiplayerScenarioException(
                $"Player motion multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new PlayerMotionMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new PlayerMotionMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private static async Task<PlayerMotionFixture> PrepareAndApplyFixtureAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int actorPlayerIndex,
        Vector3State? startPosition,
        string label,
        IReadOnlyCollection<GameNodeProcess> observationNodes,
        TimeSpan stepTimeout,
        Dictionary<string, StateSnapshotV2> snapshots,
        CancellationToken cancellationToken) {
        PlayerMotionFixture fixture = await runner.RunAsync(
            $"prepare-{label}-fixture",
            ControlCommandNames.PreparePlayerMotionFixture,
            token => cluster.Server.SendAsync<PreparePlayerMotionFixtureCommand, PlayerMotionFixture>(
                ControlCommandNames.PreparePlayerMotionFixture,
                new PreparePlayerMotionFixtureCommand(actorPlayerIndex, startPosition),
                cancellationToken: token),
            result => $"position={FormatVector(result.StartPosition)}",
            cancellationToken).ConfigureAwait(false);
        EnsureFixture(fixture, actorPlayerIndex, startPosition);

        await runner.RunAsync(
            $"apply-{label}-fixture",
            ControlCommandNames.ApplyPlayerMotionFixture,
            token => cluster.Clients[0].SendAsync<ApplyPlayerMotionFixtureCommand, ControlAck>(
                ControlCommandNames.ApplyPlayerMotionFixture,
                new ApplyPlayerMotionFixtureCommand(fixture),
                cancellationToken: token),
            cancellationToken).ConfigureAwait(false);

        ExpectedActorState expected = new(
            fixture.StartPosition,
            new QuaternionState(0, 0, 0, 1),
            fixture.InitialLookAngles,
            fixture.InitialCrouching);
        Dictionary<string, StateSnapshotV2> baseline = await runner.RunAsync(
            $"verify-{label}-baseline",
            ControlCommandNames.GetSnapshotV2,
            token => WaitForActorStateOnAllAsync(
                observationNodes,
                actorPlayerIndex,
                expected,
                PositionConvergenceTolerance,
                LookConvergenceTolerance,
                requireSettled: true,
                stepTimeout,
                token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AddSnapshots(snapshots, baseline, $"{label}-baseline");
        return fixture;
    }

    private static async Task<PlayerBodySnapshotV2> RunMotionActionAsync(
        GameNodeProcess actor,
        IReadOnlyCollection<GameNodeProcess> observationNodes,
        ScenarioRunner runner,
        int actorPlayerIndex,
        MotionCommandPlan plan,
        TimeSpan stepTimeout,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events,
        List<ScenarioActionRecordV2> actions,
        List<ScenarioAssertionRecordV2> assertions,
        CancellationToken cancellationToken) {
        StateSnapshotV2 baselineSnapshot = await WaitForActorReadyAsync(
            actor,
            actorPlayerIndex,
            stepTimeout,
            cancellationToken).ConfigureAwait(false);
        PlayerBodySnapshotV2 baseline = RequireActorBody(baselineSnapshot, actorPlayerIndex);

        ExecutedMotionAction executed = await runner.RunAsync(
            $"execute-{plan.Label}",
            CommandName(plan.Kind),
            token => ExecuteMotionActionAsync(actor, actorPlayerIndex, plan, token),
            result => $"actionId={result.Receipt.ActionId}; code={result.Receipt.Code}",
            cancellationToken).ConfigureAwait(false);
        if (!executed.Receipt.Accepted) {
            throw new InvalidOperationException(
                $"Action '{executed.Receipt.ActionId}' was rejected: {executed.Receipt.Code}.");
        }
        actions.Add(new ScenarioActionRecordV2(
            actor.Role,
            executed.Command,
            executed.Payload,
            executed.Receipt));

        ActionEventsResult actionEvents = await runner.RunAsync(
            $"verify-{plan.Label}-events",
            ControlCommandNames.GetEvents,
            token => WaitForAppliedEventAsync(
                actor,
                executed.Receipt.ActionId,
                stepTimeout,
                token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        events[$"{actor.Role}-{plan.Label}"] = actionEvents;

        StateSnapshotV2 actorFinal = await WaitForActorEffectAsync(
            actor,
            actorPlayerIndex,
            baseline,
            plan,
            stepTimeout,
            cancellationToken).ConfigureAwait(false);
        PlayerBodySnapshotV2 finalBody = RequireActorBody(actorFinal, actorPlayerIndex);
        ExpectedActorState expected = ExpectedActorState.From(finalBody);
        Dictionary<string, StateSnapshotV2> converged = await runner.RunAsync(
            $"verify-{plan.Label}-convergence",
            ControlCommandNames.GetSnapshotV2,
            token => WaitForActorStateOnAllAsync(
                observationNodes,
                actorPlayerIndex,
                expected,
                PositionConvergenceTolerance,
                LookConvergenceTolerance,
                requireSettled: true,
                stepTimeout,
                token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AddSnapshots(snapshots, converged, $"{plan.Label}-final");
        assertions.Add(new ScenarioAssertionRecordV2(
            $"{plan.Label}-authority-observer-convergence",
            "passed",
            observationNodes.Select(node => node.Role).ToArray(),
            $"playerIndex={actorPlayerIndex}; effect={plan.Kind}"));
        assertions.Add(new ScenarioAssertionRecordV2(
            $"{plan.Label}-typed-action-event",
            "passed",
            [actor.Role],
            $"actionId={executed.Receipt.ActionId}; phases=queued,observed,applied"));
        return finalBody;
    }

    private static async Task VerifyAuthorityRejectionsAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int actorPlayerIndex,
        TimeSpan stepTimeout,
        CancellationToken cancellationToken) {
        PlayerMoveCommand command = new(
            actorPlayerIndex,
            new Vector3State(0, 0, 0.5),
            HoldFrames: 10);
        await runner.RunAsync(
            "reject-server-originated-action",
            ControlCommandNames.PlayerMove,
            async token => {
                ControlCommandException exception = await AssertCommandRejectedAsync(() =>
                    cluster.Server.SendAsync<PlayerMoveCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMove,
                        command,
                        cancellationToken: token)).ConfigureAwait(false);
                EnsureRejectionCode(exception, "permission_denied");
            },
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "reject-non-owner-action",
            ControlCommandNames.PlayerMove,
            async token => {
                _ = await WaitForActorReadyAsync(
                    cluster.Clients[1],
                    actorPlayerIndex,
                    stepTimeout,
                    token).ConfigureAwait(false);
                ControlCommandException exception = await AssertCommandRejectedAsync(() =>
                    cluster.Clients[1].SendAsync<PlayerMoveCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMove,
                        command,
                        cancellationToken: token)).ConfigureAwait(false);
                EnsureRejectionCode(exception, "permission_denied");
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExecutedMotionAction> ExecuteMotionActionAsync(
        GameNodeProcess actor,
        int actorPlayerIndex,
        MotionCommandPlan plan,
        CancellationToken cancellationToken) {
        switch (plan.Kind) {
            case MotionEffectKind.Move: {
                PlayerMoveCommand command = ((PlayerMoveCommand)plan.Payload) with {
                    PlayerIndex = actorPlayerIndex
                };
                ActionReceipt receipt = await actor.SendAsync<PlayerMoveCommand, ActionReceipt>(
                    ControlCommandNames.PlayerMove,
                    command,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return Executed(ControlCommandNames.PlayerMove, command, receipt);
            }
            case MotionEffectKind.Look: {
                PlayerLookCommand command = ((PlayerLookCommand)plan.Payload) with {
                    PlayerIndex = actorPlayerIndex
                };
                ActionReceipt receipt = await actor.SendAsync<PlayerLookCommand, ActionReceipt>(
                    ControlCommandNames.PlayerLook,
                    command,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return Executed(ControlCommandNames.PlayerLook, command, receipt);
            }
            case MotionEffectKind.SetCrouch: {
                PlayerSetCrouchCommand command = ((PlayerSetCrouchCommand)plan.Payload) with {
                    PlayerIndex = actorPlayerIndex
                };
                ActionReceipt receipt = await actor.SendAsync<PlayerSetCrouchCommand, ActionReceipt>(
                    ControlCommandNames.PlayerSetCrouch,
                    command,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return Executed(ControlCommandNames.PlayerSetCrouch, command, receipt);
            }
            default:
                throw new InvalidOperationException($"Unsupported motion action '{plan.Kind}'.");
        }
    }

    private static ExecutedMotionAction Executed<TCommand>(
        string command,
        TCommand payload,
        ActionReceipt receipt) =>
        new(
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            receipt);

    private static async Task<StateSnapshotV2> WaitForActorEffectAsync(
        GameNodeProcess actor,
        int actorPlayerIndex,
        PlayerBodySnapshotV2 baseline,
        MotionCommandPlan plan,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureActorAsync(actor, actorPlayerIndex, cancellationToken)
                .ConfigureAwait(false);
            PlayerBodySnapshotV2? body = FindActorBody(last, actorPlayerIndex);
            if (body != null
                && IsSettled(body)
                && EffectObserved(plan, baseline, body)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{actor.Role}' did not expose the production effect for {plan.Label}. "
            + $"Baseline={FormatBody(baseline)}; Actual={FormatBody(FindActorBody(last, actorPlayerIndex))}.");
    }

    private static bool EffectObserved(
        MotionCommandPlan plan,
        PlayerBodySnapshotV2 baseline,
        PlayerBodySnapshotV2 actual) =>
        plan.Kind switch {
            MotionEffectKind.Move => HorizontalDistance(baseline.Position, actual.Position) >= 0.3,
            MotionEffectKind.Look => LookEffectObserved(
                (PlayerLookCommand)plan.Payload,
                baseline,
                actual),
            MotionEffectKind.SetCrouch =>
                actual.IsCrouching == ((PlayerSetCrouchCommand)plan.Payload).Enabled,
            _ => false
        };

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForActorStateOnAllAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        int actorPlayerIndex,
        ExpectedActorState expected,
        double positionTolerance,
        double lookTolerance,
        bool requireSettled,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        TimeSpan observationTimeout = timeout > TimeSpan.FromSeconds(2)
            ? timeout - TimeSpan.FromSeconds(1)
            : timeout;
        GameNodeProcess[] orderedNodes = nodes.ToArray();
        StateSnapshotV2[] captured = await Task.WhenAll(orderedNodes.Select(node =>
            WaitForActorStateAsync(
                node,
                actorPlayerIndex,
                expected,
                positionTolerance,
                lookTolerance,
                requireSettled,
                observationTimeout,
                cancellationToken))).ConfigureAwait(false);
        return orderedNodes
            .Select((node, index) => (node.Role, Snapshot: captured[index]))
            .ToDictionary(item => item.Role, item => item.Snapshot, StringComparer.Ordinal);
    }

    private static async Task<StateSnapshotV2> WaitForActorStateAsync(
        GameNodeProcess node,
        int actorPlayerIndex,
        ExpectedActorState expected,
        double positionTolerance,
        double lookTolerance,
        bool requireSettled,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureActorAsync(node, actorPlayerIndex, cancellationToken)
                .ConfigureAwait(false);
            bool uniquePlayerIndexes = last.Players
                .GroupBy(player => player.PlayerIndex)
                .All(group => group.Count() == 1);
            PlayerSnapshotV2? actor = uniquePlayerIndexes
                ? last.Players.FirstOrDefault(player => player.PlayerIndex == actorPlayerIndex)
                : null;
            PlayerBodySnapshotV2? body = actor?.Body;
            if (actor?.ReadyForPlaying == true
                && body?.LookAngles != null
                && body.Rotation != null
                && PositionDistance(body.Position, expected.Position) <= positionTolerance
                && LookDistance(body.LookAngles, expected.LookAngles) <= lookTolerance
                && RotationDistance(body.Rotation, expected.Rotation)
                    <= RotationConvergenceTolerance
                && body.IsCrouching == expected.IsCrouching
                && (!requireSettled || IsSettled(body))) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge player {actorPlayerIndex}. "
            + $"Expected={expected}; Actual={FormatBody(FindActorBody(last, actorPlayerIndex))}; "
            + $"Players={FormatPlayers(last)}.");
    }

    private static async Task<StateSnapshotV2> WaitForActorReadyAsync(
        GameNodeProcess node,
        int actorPlayerIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureActorAsync(node, actorPlayerIndex, cancellationToken)
                .ConfigureAwait(false);
            PlayerSnapshotV2? actor = last.Players.FirstOrDefault(player =>
                player.PlayerIndex == actorPlayerIndex);
            if (actor?.ReadyForPlaying == true
                && actor.Body?.LookAngles != null
                && actor.Body.Rotation != null) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose ready player {actorPlayerIndex}. "
            + $"Players={FormatPlayers(last)}.");
    }

    private static Task<StateSnapshotV2> CaptureActorAsync(
        GameNodeProcess node,
        int actorPlayerIndex,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Players],
                PlayerIndexes: [actorPlayerIndex]),
            cancellationToken: cancellationToken);

    private static async Task<ActionEventsResult> WaitForAppliedEventAsync(
        GameNodeProcess actor,
        string actionId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        ActionEventsResult? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await actor.SendAsync<GetEventsCommand, ActionEventsResult>(
                ControlCommandNames.GetEvents,
                new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            ActionEventRecord[] actionEvents = last.Events
                .Where(item => string.Equals(item.ActionId, actionId, StringComparison.Ordinal))
                .ToArray();
            ActionEventRecord? rejected = actionEvents.FirstOrDefault(item =>
                item.Phase == ActionEventPhase.Rejected);
            if (rejected != null) {
                throw new InvalidOperationException(
                    $"Action '{actionId}' was rejected by the input adapter: {rejected.Code}.");
            }
            if (actionEvents.Any(item => item.Phase == ActionEventPhase.Queued)
                && actionEvents.Any(item => item.Phase == ActionEventPhase.Observed)
                && actionEvents.Any(item => item.Phase == ActionEventPhase.Applied)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        string phases = last == null
            ? "missing"
            : string.Join(',', last.Events
                .Where(item => item.ActionId == actionId)
                .Select(item => item.Phase));
        throw new TimeoutException(
            $"Action '{actionId}' did not publish queued, observed and applied events. "
            + $"Phases={phases}.");
    }

    private static async Task ConfigureNetworkFaultsAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        NetworkFaultProfile baseProfile,
        CancellationToken cancellationToken) {
        GameNodeProcess[] orderedNodes = nodes.ToArray();
        NetworkFaultProfile[] expectedProfiles = orderedNodes
            .Select((_, index) => baseProfile with {
                Seed = unchecked(baseProfile.Seed + index)
            })
            .ToArray();
        NetworkFaultState[] states = await Task.WhenAll(orderedNodes.Select((node, index) =>
            node.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(expectedProfiles[index]),
                cancellationToken: cancellationToken))).ConfigureAwait(false);
        for (int index = 0; index < states.Length; index++) {
            if (states[index].Profile != expectedProfiles[index]) {
                throw new InvalidOperationException(
                    $"Node '{orderedNodes[index].Role}' did not apply its deterministic fault profile.");
            }
        }
    }

    private static long VerifyNetworkFaultEvidence(
        IReadOnlyDictionary<string, StateSnapshotV2> snapshots,
        IReadOnlyCollection<GameNodeProcess> nodes,
        string actionName) {
        Dictionary<string, long> duplicatedByRole = new(StringComparer.Ordinal);
        foreach (GameNodeProcess node in nodes) {
            string key = $"{node.Role}-{actionName}-final";
            if (!snapshots.TryGetValue(key, out StateSnapshotV2? snapshot)
                || snapshot.Network == null) {
                throw new InvalidOperationException(
                    $"Network fault evidence snapshot '{key}' is missing.");
            }
            duplicatedByRole[node.Role] = snapshot.Network.FaultDuplicated;
        }
        foreach (string requiredRole in new[] { "server", "client-a" }) {
            if (!duplicatedByRole.TryGetValue(requiredRole, out long count) || count <= 0) {
                throw new InvalidOperationException(
                    $"Node '{requiredRole}' did not report duplicated packets during {actionName}.");
            }
        }
        return duplicatedByRole.Values.Sum();
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

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            try {
                snapshots[$"{node.Role}-final"] =
                    await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                        ControlCommandNames.GetSnapshotV2,
                        new GetSnapshotV2Command(
                            [SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.Events],
                            EventLimit: AutomationProtocol.MaximumEventQueryCount),
                        timeout: TimeSpan.FromSeconds(5),
                        cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Evidence collection must not replace the original scenario failure.
            }
            try {
                events[$"{node.Role}-final"] = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                    ControlCommandNames.GetEvents,
                    new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // A dead node can still contribute retained process logs.
            }
        }
    }

    private static PlayerMotionReplayPlan CreateDefaultActionPlan(int actorPlayerIndex) =>
        new(
            new PlayerMoveCommand(
                actorPlayerIndex,
                new Vector3State(0, 0, 0.75),
                HoldFrames: 45),
            new PlayerLookCommand(
                actorPlayerIndex,
                new Vector2State(0.6, -0.35),
                HoldFrames: 10),
            new PlayerSetCrouchCommand(actorPlayerIndex, true),
            new PlayerLookCommand(
                actorPlayerIndex,
                new Vector2State(-0.45, 0.25),
                HoldFrames: 10),
            new PlayerSetCrouchCommand(actorPlayerIndex, true));

    private static void ValidateReplayPlan(PlayerMotionReplayPlan? replay) {
        if (replay == null) {
            return;
        }
        ValidateMove(replay.InitialMove);
        ValidateLook(replay.InitialLook, "initialLook");
        ValidateLook(replay.PersistenceLook, "persistenceLook");
        if (!replay.InitialCrouch.Enabled || !replay.PersistenceCrouch.Enabled) {
            throw new InvalidDataException(
                "Replay crouch actions must enable crouch from the fixture's standing state.");
        }
    }

    private static void ValidateMove(PlayerMoveCommand command) {
        if (command.Input == null
            || command.HoldFrames is < 1 or > 600
            || !IsFinite(command.Input)
            || Math.Abs(command.Input.Y) > 0.0001
            || HorizontalLengthSquared(command.Input) is <= 0.0001 or > 1.0001) {
            throw new InvalidDataException("Replay move action has invalid input or holdFrames.");
        }
    }

    private static void ValidateLook(PlayerLookCommand command, string name) {
        if (command.Input == null
            || command.HoldFrames is < 1 or > 600
            || !IsFinite(command.Input)
            || LengthSquared(command.Input) is <= 0.0001 or > 1.0001) {
            throw new InvalidDataException($"Replay {name} action has invalid input or holdFrames.");
        }
    }

    private static void EnsureFixture(
        PlayerMotionFixture fixture,
        int actorPlayerIndex,
        Vector3State? requestedStart) {
        if (fixture.PlayerIndex != actorPlayerIndex
            || fixture.StartPosition == null
            || fixture.InitialLookAngles == null
            || fixture.InitialCrouching
            || fixture.StandingValue <= 0) {
            throw new InvalidOperationException(
                $"Motion fixture is invalid for actor {actorPlayerIndex}: {fixture}.");
        }
        if (requestedStart != null
            && HorizontalDistance(fixture.StartPosition, requestedStart) > 0.01) {
            throw new InvalidOperationException(
                $"Fixture changed the requested horizontal position. "
                + $"Requested={FormatVector(requestedStart)}; "
                + $"Actual={FormatVector(fixture.StartPosition)}.");
        }
    }

    private static void EnsureActorIdentity(
        NodeStatus status,
        int expectedPlayerIndex,
        string context) {
        if (status.PlayerIndex < 0 || !status.MainPlayerReady) {
            throw new InvalidOperationException(
                $"Actor was not ready after {context}. Status={status}.");
        }
        if (status.PlayerIndex != expectedPlayerIndex) {
            throw new InvalidOperationException(
                $"Actor identity changed from player {expectedPlayerIndex} "
                + $"to {status.PlayerIndex} after {context}.");
        }
    }

    private static PlayerBodySnapshotV2 RequireActorBody(
        StateSnapshotV2 snapshot,
        int actorPlayerIndex) =>
        FindActorBody(snapshot, actorPlayerIndex)
        ?? throw new InvalidOperationException(
            $"Snapshot '{snapshot.CaptureId}' does not contain body state for player "
            + $"{actorPlayerIndex}.");

    private static PlayerBodySnapshotV2? FindActorBody(
        StateSnapshotV2? snapshot,
        int actorPlayerIndex) =>
        snapshot?.Players
            .FirstOrDefault(player => player.PlayerIndex == actorPlayerIndex)
            ?.Body;

    private static bool IsSettled(PlayerBodySnapshotV2 body) =>
        Math.Sqrt(
            body.Velocity.X * body.Velocity.X
            + body.Velocity.Y * body.Velocity.Y
            + body.Velocity.Z * body.Velocity.Z) <= SettledVelocity;

    private static double PositionDistance(Vector3State first, Vector3State second) {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        double z = first.Z - second.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    private static double HorizontalDistance(Vector3State first, Vector3State second) {
        double x = first.X - second.X;
        double z = first.Z - second.Z;
        return Math.Sqrt(x * x + z * z);
    }

    private static double LookDistance(Vector2State first, Vector2State second) {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static double RotationDistance(QuaternionState first, QuaternionState second) {
        double firstLength = Math.Sqrt(
            first.X * first.X
            + first.Y * first.Y
            + first.Z * first.Z
            + first.W * first.W);
        double secondLength = Math.Sqrt(
            second.X * second.X
            + second.Y * second.Y
            + second.Z * second.Z
            + second.W * second.W);
        if (firstLength <= double.Epsilon || secondLength <= double.Epsilon) {
            return double.PositiveInfinity;
        }
        double dot = Math.Abs(
            (first.X * second.X
             + first.Y * second.Y
             + first.Z * second.Z
             + first.W * second.W)
            / (firstLength * secondLength));
        return 2 * Math.Acos(Math.Clamp(dot, 0, 1));
    }

    private static bool LookEffectObserved(
        PlayerLookCommand command,
        PlayerBodySnapshotV2 baseline,
        PlayerBodySnapshotV2 actual) {
        if (baseline.LookAngles == null
            || actual.LookAngles == null
            || baseline.Rotation == null
            || actual.Rotation == null) {
            return false;
        }
        bool pitchObserved = Math.Abs(command.Input.Y) <= 0.0001
            || LookDistance(baseline.LookAngles, actual.LookAngles) >= 0.05;
        bool yawObserved = Math.Abs(command.Input.X) <= 0.0001
            || RotationDistance(baseline.Rotation, actual.Rotation) >= 0.05;
        return pitchObserved && yawObserved;
    }

    private static double HorizontalLengthSquared(Vector3State value) =>
        value.X * value.X + value.Z * value.Z;

    private static double LengthSquared(Vector2State value) =>
        value.X * value.X + value.Y * value.Y;

    private static bool IsFinite(Vector3State value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static bool IsFinite(Vector2State value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y);

    private static string CommandName(MotionEffectKind kind) =>
        kind switch {
            MotionEffectKind.Move => ControlCommandNames.PlayerMove,
            MotionEffectKind.Look => ControlCommandNames.PlayerLook,
            MotionEffectKind.SetCrouch => ControlCommandNames.PlayerSetCrouch,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static TCommand DeserializeAction<TCommand>(ScenarioActionRecordV2 record) =>
        record.Payload.Deserialize<TCommand>(ControlProtocol.JsonOptions)
        ?? throw new InvalidDataException(
            $"Replay action '{record.Receipt.ActionId}' has an invalid "
            + $"{typeof(TCommand).Name} payload.");

    private static void AddSnapshots(
        IDictionary<string, StateSnapshotV2> destination,
        IReadOnlyDictionary<string, StateSnapshotV2> source,
        string suffix) {
        foreach ((string role, StateSnapshotV2 snapshot) in source) {
            destination[$"{role}-{suffix}"] = snapshot;
        }
    }

    private static string FormatBody(PlayerBodySnapshotV2? body) =>
        body == null
            ? "missing"
            : $"position={FormatVector(body.Position)}; velocity={FormatVector(body.Velocity)}; "
              + $"rotation={FormatQuaternion(body.Rotation)}; "
              + $"look={FormatVector(body.LookAngles)}; crouch={body.IsCrouching}";

    private static string FormatPlayers(StateSnapshotV2? snapshot) =>
        snapshot == null
            ? "missing"
            : string.Join(", ", snapshot.Players.Select(player =>
                $"{player.PlayerIndex}:{player.Name}:ready={player.ReadyForPlaying}:entity={player.EntityId}"));

    private static string FormatVector(Vector3State value) =>
        FormattableString.Invariant($"{value.X:R},{value.Y:R},{value.Z:R}");

    private static string FormatVector(Vector2State? value) =>
        value == null
            ? "missing"
            : FormattableString.Invariant($"{value.X:R},{value.Y:R}");

    private static string FormatQuaternion(QuaternionState? value) =>
        value == null
            ? "missing"
            : FormattableString.Invariant(
                $"{value.X:R},{value.Y:R},{value.Z:R},{value.W:R}");

    private static bool IsFaultProfileEnabled(NetworkFaultProfile profile) =>
        profile.BaseLatencyMilliseconds > 0
        || profile.JitterMilliseconds > 0
        || profile.LossRate > 0
        || profile.DuplicationRate > 0
        || profile.ReorderRate > 0
        || profile.BandwidthBytesPerSecond > 0;

    private static string FormatFaultProfile(NetworkFaultProfile profile) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"seed={profile.Seed}; latencyMs={profile.BaseLatencyMilliseconds}; jitterMs={profile.JitterMilliseconds}; loss={profile.LossRate:R}; duplicate={profile.DuplicationRate:R}; reorder={profile.ReorderRate:R}; reorderDelayMs={profile.ReorderDelayMilliseconds}; bandwidth={profile.BandwidthBytesPerSecond}");

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
            // Preserving an isolated run directory is safer than hiding scenario results.
        }
    }

    private enum MotionEffectKind {
        Move,
        Look,
        SetCrouch
    }

    private sealed record MotionCommandPlan(
        string Label,
        MotionEffectKind Kind,
        object Payload);

    private sealed record ExecutedMotionAction(
        string Command,
        JsonElement Payload,
        ActionReceipt Receipt);

    private sealed record ExpectedActorState(
        Vector3State Position,
        QuaternionState Rotation,
        Vector2State LookAngles,
        bool IsCrouching) {
        public static ExpectedActorState From(PlayerBodySnapshotV2 body) =>
            new(
                body.Position,
                body.Rotation
                    ?? throw new InvalidOperationException("Player body snapshot lacks rotation."),
                body.LookAngles
                    ?? throw new InvalidOperationException("Player body snapshot lacks look angles."),
                body.IsCrouching);
    }
}
