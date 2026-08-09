using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record LogicReplayPlan(
    LogicEditCommand ForgedState,
    LogicEditCommand FarEdit,
    LogicInteractCommand CrossPlayerInteract,
    LogicEditCommand ObserverEdit,
    LogicEditCommand[] Edits,
    LogicInteractCommand Toggle,
    RunLogicRevisionBurstCommand RevisionBurst,
    RunMovingBlockLifecycleBurstCommand MovingBurst,
    RunPistonLifecycleCommand PistonLifecycle);

public sealed record LogicMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    LogicReplayPlan? ReplayPlan = null);

public sealed record LogicMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class LogicMultiplayerScenarioException : Exception {
    public LogicMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class LogicMultiplayerScenario {
    public const string ScenarioName = "logic-multiplayer";
    public const int ScenarioVersion = 1;
    public const int ExpectedActionCount = 17;

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
        new("verify-control-authority", "typed authority rejection"),
        new("prepare-logic-fixture", ControlCommandNames.PrepareLogicFixture),
        new("apply-client-fixtures", ControlCommandNames.ApplyLogicFixture),
        new("verify-security-probes", ControlCommandNames.LogicEdit),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("edit-all-logic-elements", ControlCommandNames.LogicEdit),
        new("toggle-switch-idempotently", ControlCommandNames.LogicInteract),
        new("run-logic-revision-burst", ControlCommandNames.RunLogicRevisionBurst),
        new("run-moving-block-lifecycle-burst", ControlCommandNames.RunMovingBlockLifecycleBurst),
        new("run-piston-lifecycle", ControlCommandNames.RunPistonLifecycle),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("connect-late-client", "network.connect(client-c)"),
        new("apply-late-client-view", ControlCommandNames.ApplyTerrainStreamingView),
        new("verify-late-join", ControlCommandNames.GetSnapshotV2),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("reconnect-actor", "network.connect(client-a)"),
        new("apply-reconnected-actor-view", ControlCommandNames.ApplyTerrainStreamingView),
        new("verify-reconnect", ControlCommandNames.GetSnapshotV2),
        new("save-and-stop-world", ControlCommandNames.SaveWorld),
        new("reload-saved-world", ControlCommandNames.ReloadWorld),
        new("restart-transport-host", "network.startHost"),
        new("connect-persistence-clients", "network.connect(client-a, client-c)"),
        new("apply-persistence-client-views", ControlCommandNames.ApplyTerrainStreamingView),
        new("verify-persistence-boundary", ControlCommandNames.GetSnapshotV2)
    ];

    public static NetworkFaultProfile CreateDefaultNetworkFaults(int seed) =>
        new(
            Seed: unchecked(seed + 919),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18);

    public static LogicReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        (string Role, string Command)[] expected = [
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicInteract),
            ("client-b", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicEdit),
            ("client-a", ControlCommandNames.LogicInteract),
            ("server", ControlCommandNames.RunLogicRevisionBurst),
            ("server", ControlCommandNames.RunMovingBlockLifecycleBurst),
            ("server", ControlCommandNames.RunPistonLifecycle)
        ];
        if (records.Count != expected.Length) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly {expected.Length} actions.");
        }
        for (int index = 0; index < expected.Length; index++) {
            if (!string.Equals(records[index].Role, expected[index].Role, StringComparison.Ordinal)
                || !string.Equals(records[index].Command, expected[index].Command, StringComparison.Ordinal)) {
                throw new InvalidDataException(
                    $"Replay action {index} must be {expected[index].Role}/{expected[index].Command}.");
            }
        }

        LogicReplayPlan plan = new(
            DeserializeAction<LogicEditCommand>(records[0]),
            DeserializeAction<LogicEditCommand>(records[1]),
            DeserializeAction<LogicInteractCommand>(records[2]),
            DeserializeAction<LogicEditCommand>(records[3]),
            records.Skip(4).Take(9).Select(DeserializeAction<LogicEditCommand>).ToArray(),
            DeserializeAction<LogicInteractCommand>(records[13]),
            DeserializeAction<RunLogicRevisionBurstCommand>(records[14]),
            DeserializeAction<RunMovingBlockLifecycleBurstCommand>(records[15]),
            DeserializeAction<RunPistonLifecycleCommand>(records[16]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<LogicMultiplayerScenarioResult> RunAsync(
        LogicMultiplayerScenarioOptions options,
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
                "Logic resilience requires a non-empty network fault profile.",
                nameof(options.NetworkFaults));
        }

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(12);
        if (stepTimeout <= TimeSpan.Zero || scenarioTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(
                stepTimeout <= TimeSpan.Zero ? nameof(options.StepTimeout) : nameof(options.ScenarioTimeout));
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
        TerrainCellCoordinate[] logicCells = [];
        LogicRevisionBurstResult? revisionResult = null;
        MovingBlockLifecycleBurstResult? movingResult = null;
        PistonLifecycleResult? pistonResult = null;

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
                    ControlCommandNames.PrepareLogicFixture,
                    ControlCommandNames.ApplyLogicFixture,
                    ControlCommandNames.ApplyTerrainStreamingView,
                    ControlCommandNames.LogicEdit,
                    ControlCommandNames.LogicInteract,
                    ControlCommandNames.RunLogicRevisionBurst,
                    ControlCommandNames.RunMovingBlockLifecycleBurst,
                    ControlCommandNames.RunPistonLifecycle
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Logic
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"Logic-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            NodeStatus observerStatus = await cluster.Clients[1].GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);
            if (observerStatus.PlayerIndex < 0
                || observerStatus.PlayerIndex == actorPlayerIndex) {
                throw new InvalidOperationException(
                    $"Observer did not expose a distinct player identity: {observerStatus}.");
            }

            await VerifyControlAuthorityAsync(cluster, runner, actorPlayerIndex, scenarioToken)
                .ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-control-authority-enforcement",
                "passed",
                [cluster.Server.Role, cluster.Clients[0].Role, cluster.Clients[1].Role],
                "clients cannot create authoritative fixtures or bursts and the server cannot originate player edit commands"));
            await runner.RunAsync(
                "reset-events",
                ControlCommandNames.ResetEvents,
                token => Task.WhenAll(MultiplayerGameplayBootstrap.AllNodes(cluster).Select(node =>
                    node.SendAsync<EmptyCommand, ResetEventsResult>(
                        ControlCommandNames.ResetEvents,
                        new EmptyCommand(),
                        cancellationToken: token))),
                scenarioToken).ConfigureAwait(false);

            LogicFixture fixture = await runner.RunAsync(
                "prepare-logic-fixture",
                ControlCommandNames.PrepareLogicFixture,
                token => cluster.Server.SendAsync<PrepareLogicFixtureCommand, LogicFixture>(
                    ControlCommandNames.PrepareLogicFixture,
                    new PrepareLogicFixtureCommand(actorPlayerIndex),
                    cancellationToken: token),
                value => $"elements={value.Elements.Length}; actor={value.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            ValidateFixture(fixture, actorPlayerIndex);
            logicCells = fixture.Elements
                .Select(element => element.Cell)
                .Append(fixture.FarBatteryCell)
                .Append(fixture.PersistentVoltageCell)
                .Distinct()
                .ToArray();

            LogicFixture observerFixture = fixture with {
                PlayerIndex = observerStatus.PlayerIndex,
                PlayerEntityId = 0,
                PlayerPosition = new Vector3State(
                    fixture.PlayerPosition.X + 24,
                    fixture.PlayerPosition.Y,
                    fixture.PlayerPosition.Z + 24)
            };
            await runner.RunAsync(
                "apply-client-fixtures",
                ControlCommandNames.ApplyLogicFixture,
                token => Task.WhenAll(
                    cluster.Clients[0].SendAsync<ApplyLogicFixtureCommand, ControlAck>(
                        ControlCommandNames.ApplyLogicFixture,
                        new ApplyLogicFixtureCommand(fixture),
                        cancellationToken: token),
                    cluster.Clients[1].SendAsync<ApplyLogicFixtureCommand, ControlAck>(
                        ControlCommandNames.ApplyLogicFixture,
                        new ApplyLogicFixtureCommand(observerFixture),
                        cancellationToken: token)),
                scenarioToken).ConfigureAwait(false);

            GameNodeProcess[] initialNodes = [
                cluster.Server,
                cluster.Clients[0],
                cluster.Clients[1]
            ];
            StateSnapshotV2[] initialSnapshots = await WaitForLogicConvergenceAsync(
                initialNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true).ConfigureAwait(false);
            AddSnapshots(snapshots, "initial", initialSnapshots, initialNodes);
            StateSnapshotV2 securityBaseline = initialSnapshots[0];

            LogicReplayPlan generatedPlan = CreateDefaultPlan(
                fixture,
                observerStatus.PlayerIndex);
            LogicReplayPlan plan = options.ReplayPlan ?? generatedPlan;
            if (options.ReplayPlan != null) {
                RequireEquivalentReplayPlan(generatedPlan, options.ReplayPlan);
            }

            ActionReceipt forgedReceipt = await SendLogicEditAsync(
                runner,
                cluster.Clients[0],
                "probe-forged-state",
                plan.ForgedState,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.LogicEdit,
                plan.ForgedState, forgedReceipt));
            ActionReceipt farReceipt = await SendLogicEditAsync(
                runner,
                cluster.Clients[0],
                "probe-out-of-range-edit",
                plan.FarEdit,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.LogicEdit,
                plan.FarEdit, farReceipt));
            ActionReceipt crossReceipt = await SendLogicInteractAsync(
                runner,
                cluster.Clients[0],
                "probe-cross-player-interaction",
                plan.CrossPlayerInteract,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.LogicInteract,
                plan.CrossPlayerInteract, crossReceipt));
            ActionReceipt observerReceipt = await SendLogicEditAsync(
                runner,
                cluster.Clients[1],
                "probe-observer-out-of-range-edit",
                plan.ObserverEdit,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[1].Role, ControlCommandNames.LogicEdit,
                plan.ObserverEdit, observerReceipt));
            await Task.Delay(TimeSpan.FromMilliseconds(500), scenarioToken).ConfigureAwait(false);
            StateSnapshotV2[] securitySnapshots = await WaitForLogicConvergenceAsync(
                initialNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true).ConfigureAwait(false);
            SnapshotV2Diff securityDiff = SnapshotV2Comparer.CompareLogic(
                securityBaseline,
                securitySnapshots[0],
                new SnapshotV2LogicComparisonOptions(CompareRevisions: false));
            EnsureEqual(securityDiff, "Security probes changed authoritative logic state");
            AddSnapshots(snapshots, "after-security-probes", securitySnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-session-range-and-sender-authority",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "forged authoritative state, out-of-range edit, cross-player interaction, and observer edit left authoritative state unchanged"));

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            for (int index = 0; index < plan.Edits.Length; index++) {
                LogicEditCommand edit = plan.Edits[index];
                ActionReceipt receipt = await SendLogicEditAsync(
                    runner,
                    cluster.Clients[0],
                    $"edit-{index:D2}-{edit.Kind}",
                    edit,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
                actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.LogicEdit,
                    edit, receipt));
                LogicFixtureElement expectedElement = fixture.Elements.Single(element =>
                    element.Kind == edit.Kind && element.Cell == edit.Cell);
                _ = await WaitForLogicConvergenceAsync(
                    initialNodes,
                    logicCells,
                    stepTimeout,
                    scenarioToken,
                    compareRevisions: true,
                    serverPredicate: snapshot => ElementTargetApplied(snapshot, expectedElement))
                    .ConfigureAwait(false);
            }
            StateSnapshotV2[] editedSnapshots = await WaitForLogicConvergenceAsync(
                initialNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => FixtureTargetsApplied(snapshot, fixture))
                .ConfigureAwait(false);
            AddSnapshots(snapshots, "after-edits", editedSnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-nine-element-authoritative-edit-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "battery, button, switch, delay gate, piston, memory bank, truth table, sign, and dispenser converged under duplicate/reordered delivery"));

            ActionReceipt toggleReceipt = await SendLogicInteractAsync(
                runner,
                cluster.Clients[0],
                "toggle-switch-idempotently",
                plan.Toggle,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.LogicInteract,
                plan.Toggle, toggleReceipt));
            LogicFixtureElement switchElement = fixture.Elements.Single(element =>
                element.Kind == LogicElementKind.Switch);
            StateSnapshotV2[] toggledSnapshots = await WaitForLogicConvergenceAsync(
                initialNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => FindLogic(snapshot, switchElement.Cell).Data
                    == fixture.ToggledSwitchData).ConfigureAwait(false);
            AddSnapshots(snapshots, "after-idempotent-toggle", toggledSnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-interaction-request-idempotency",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"duplicated switch request produced exactly one transition to data={fixture.ToggledSwitchData}"));

            revisionResult = await runner.RunAsync(
                "run-logic-revision-burst",
                ControlCommandNames.RunLogicRevisionBurst,
                token => cluster.Server.SendAsync<RunLogicRevisionBurstCommand, LogicRevisionBurstResult>(
                    ControlCommandNames.RunLogicRevisionBurst,
                    plan.RevisionBurst,
                    cancellationToken: token),
                value => $"logic={value.StaleLogicRevision}->{value.FinalLogicRevision}; electricity={value.StaleElectricityRevision}->{value.FinalElectricityRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(revisionResult.Receipt);
            ValidateRevisionResult(revisionResult);
            actions.Add(ActionRecord(cluster.Server.Role, ControlCommandNames.RunLogicRevisionBurst,
                plan.RevisionBurst, revisionResult.Receipt));
            StateSnapshotV2[] revisionSnapshots = await WaitForLogicConvergenceAsync(
                initialNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => BatteryAndVoltageMatch(snapshot, revisionResult))
                .ConfigureAwait(false);
            AddSnapshots(snapshots, "after-revision-burst", revisionSnapshots, initialNodes);

            movingResult = await runner.RunAsync(
                "run-moving-block-lifecycle-burst",
                ControlCommandNames.RunMovingBlockLifecycleBurst,
                token => cluster.Server.SendAsync<RunMovingBlockLifecycleBurstCommand, MovingBlockLifecycleBurstResult>(
                    ControlCommandNames.RunMovingBlockLifecycleBurst,
                    plan.MovingBurst,
                    cancellationToken: token),
                value => $"generation={value.StaleGeneration}->{value.FinalGeneration}; revision={value.FinalRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(movingResult.Receipt);
            ValidateMovingResult(movingResult);
            actions.Add(ActionRecord(cluster.Server.Role,
                ControlCommandNames.RunMovingBlockLifecycleBurst,
                plan.MovingBurst,
                movingResult.Receipt));
            StateSnapshotV2[] movingSnapshots = await WaitForLogicConvergenceAsync(
                initialNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => MovingReplacementMatches(snapshot, movingResult))
                .ConfigureAwait(false);
            AddSnapshots(snapshots, "after-moving-lifecycle-burst", movingSnapshots, initialNodes);

            pistonResult = await runner.RunAsync(
                "run-piston-lifecycle",
                ControlCommandNames.RunPistonLifecycle,
                token => cluster.Server.SendAsync<RunPistonLifecycleCommand, PistonLifecycleResult>(
                    ControlCommandNames.RunPistonLifecycle,
                    plan.PistonLifecycle,
                    cancellationToken: token),
                value => $"observed={value.MovingObserved}; present={value.PresentAfterCommand}; generation={value.Generation}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(pistonResult.Receipt);
            if (!pistonResult.MovingObserved || pistonResult.PresentAfterCommand) {
                throw new InvalidOperationException(
                    $"Piston lifecycle did not expose add/remove boundaries: {pistonResult}.");
            }
            actions.Add(ActionRecord(cluster.Server.Role, ControlCommandNames.RunPistonLifecycle,
                plan.PistonLifecycle, pistonResult.Receipt));
            await RequireClientRejectionMetricsAsync(
                initialNodes.Skip(1).ToArray(),
                logicCells,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2[] faultSnapshots = await WaitForLogicConvergenceAsync(
                initialNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => !HasPistonMovingSet(snapshot, plan.PistonLifecycle.PistonCell))
                .ConfigureAwait(false);
            AddSnapshots(snapshots, "fault-convergence", faultSnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-stale-duplicate-reorder-lifecycle-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "final logic/electricity revisions won, stale moving generations were rejected, and a real piston add/remove lifecycle completed"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(Seed: options.Seed),
                    token),
                scenarioToken).ConfigureAwait(false);

            NodeStatus lateStatus = await runner.RunAsync(
                "connect-late-client",
                "network.connect(client-c)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        cluster.Clients[2],
                        "LearnerC",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    return await cluster.Clients[2].GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"playerIndex={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            _ = await cluster.Server.WaitForStatusAsync(
                status => status.ServerSessionCount == 3,
                stepTimeout,
                "Server did not expose the late-join session.",
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "apply-late-client-view",
                ControlCommandNames.ApplyTerrainStreamingView,
                token => ApplyLogicViewAsync(
                    cluster.Clients[2],
                    lateStatus.PlayerIndex,
                    fixture,
                    positionOffset: 8,
                    token),
                scenarioToken).ConfigureAwait(false);
            GameNodeProcess[] allNodes = [cluster.Server, .. cluster.Clients];
            StateSnapshotV2[] lateSnapshots = await WaitForLogicConvergenceAsync(
                allNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => BatteryAndVoltageMatch(snapshot, revisionResult)
                    && MovingReplacementMatches(snapshot, movingResult))
                .ConfigureAwait(false);
            AddSnapshots(snapshots, "late-join", lateSnapshots, allNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-late-join-authoritative-baseline",
                "passed",
                allNodes.Select(node => node.Role).ToArray(),
                "late client received editable logic, electricity, and active moving-block generation baselines"));

            GameNodeProcess actorClient = cluster.Clients[0];
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
            NodeStatus reconnectStatus = await runner.RunAsync(
                "reconnect-actor",
                "network.connect(client-a)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        actorClient,
                        "LearnerA",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    return await actorClient.GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"playerIndex={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            if (reconnectStatus.PlayerIndex != actorPlayerIndex) {
                throw new InvalidOperationException(
                    $"Actor identity changed across reconnect: {actorPlayerIndex}->{reconnectStatus.PlayerIndex}.");
            }
            await runner.RunAsync(
                "apply-reconnected-actor-view",
                ControlCommandNames.ApplyTerrainStreamingView,
                token => ApplyLogicViewAsync(
                    actorClient,
                    reconnectStatus.PlayerIndex,
                    fixture,
                    positionOffset: 0,
                    token),
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2[] reconnectSnapshots = await WaitForLogicConvergenceAsync(
                allNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => BatteryAndVoltageMatch(snapshot, revisionResult)
                    && MovingReplacementMatches(snapshot, movingResult))
                .ConfigureAwait(false);
            AddSnapshots(snapshots, "reconnect", reconnectSnapshots, allNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-reconnect-authoritative-baseline",
                "passed",
                allNodes.Select(node => node.Role).ToArray(),
                "reconnected actor recovered all semantic logic state, electricity state, revisions, and moving generation"));

            StateSnapshotV2 beforeReload = reconnectSnapshots[0];
            string savedWorldDirectory = await SaveStopAndReloadAsync(
                cluster,
                runner,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            _ = await runner.RunAsync(
                "restart-transport-host",
                "network.startHost",
                token => cluster.StartHostAsync(stepTimeout, token),
                room => room ?? "direct",
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "connect-persistence-clients",
                "network.connect(client-a, client-c)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        cluster.Clients[0],
                        "LearnerA",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        cluster.Clients[2],
                        "LearnerC",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    _ = await cluster.Server.WaitForStatusAsync(
                        status => status.ServerSessionCount == 2,
                        stepTimeout,
                        "Server did not expose persistence client sessions.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            NodeStatus persistenceActorStatus = await cluster.Clients[0]
                .GetStatusAsync(scenarioToken).ConfigureAwait(false);
            NodeStatus persistenceLateStatus = await cluster.Clients[2]
                .GetStatusAsync(scenarioToken).ConfigureAwait(false);
            if (persistenceActorStatus.PlayerIndex != actorPlayerIndex) {
                throw new InvalidOperationException(
                    $"Actor identity changed across persistence reload: "
                    + $"{actorPlayerIndex}->{persistenceActorStatus.PlayerIndex}.");
            }
            await runner.RunAsync(
                "apply-persistence-client-views",
                ControlCommandNames.ApplyTerrainStreamingView,
                token => Task.WhenAll(
                    ApplyLogicViewAsync(
                        cluster.Clients[0],
                        persistenceActorStatus.PlayerIndex,
                        fixture,
                        positionOffset: 0,
                        token),
                    ApplyLogicViewAsync(
                        cluster.Clients[2],
                        persistenceLateStatus.PlayerIndex,
                        fixture,
                        positionOffset: 8,
                        token)),
                scenarioToken).ConfigureAwait(false);
            GameNodeProcess[] persistenceNodes = [
                cluster.Server,
                cluster.Clients[0],
                cluster.Clients[2]
            ];
            StateSnapshotV2[] persistenceSnapshots = await WaitForLogicConvergenceAsync(
                persistenceNodes,
                logicCells,
                stepTimeout,
                scenarioToken,
                compareRevisions: true,
                serverPredicate: snapshot => PersistedLogicMatches(snapshot, fixture, revisionResult))
                .ConfigureAwait(false);
            AddSnapshots(snapshots, "after-persistence", persistenceSnapshots, persistenceNodes);
            SnapshotV2Diff persistenceDiff = SnapshotV2Comparer.CompareLogic(
                beforeReload with { MovingBlocks = [] },
                persistenceSnapshots[0] with { MovingBlocks = [] },
                new SnapshotV2LogicComparisonOptions(CompareRevisions: false));
            EnsureEqual(persistenceDiff, "Saved logic semantics changed after reload");
            assertions.Add(new ScenarioAssertionRecordV2(
                "logic-save-reload-persistence",
                "passed",
                persistenceNodes.Select(node => node.Role).ToArray(),
                $"world={savedWorldDirectory}; all editable data and persistent electricity survived save/reload, while fresh tracker revisions converged"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(cluster, logicCells, snapshots, events)
                    .ConfigureAwait(false);
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
            if (revisionResult != null) {
                metrics["logic.finalRevision"] = revisionResult.FinalLogicRevision.ToString(CultureInfo.InvariantCulture);
                metrics["electricity.finalRevision"] = revisionResult.FinalElectricityRevision.ToString(CultureInfo.InvariantCulture);
            }
            if (movingResult != null) {
                metrics["moving.finalGeneration"] = movingResult.FinalGeneration.ToString(CultureInfo.InvariantCulture);
                metrics["moving.finalRevision"] = movingResult.FinalRevision.ToString(CultureInfo.InvariantCulture);
            }
            if (pistonResult != null) {
                metrics["piston.generation"] = pistonResult.Generation.ToString(CultureInfo.InvariantCulture);
            }
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
            throw new LogicMultiplayerScenarioException(
                $"Logic multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new LogicMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        if (actions.Count != ExpectedActionCount) {
            throw new LogicMultiplayerScenarioException(
                $"Scenario produced {actions.Count} actions instead of {ExpectedActionCount}.",
                artifactDirectory,
                new InvalidDataException("Strict replay action count mismatch."));
        }
        return new LogicMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private static LogicReplayPlan CreateDefaultPlan(
        LogicFixture fixture,
        int observerPlayerIndex) {
        LogicFixtureElement battery = fixture.Elements.Single(element =>
            element.Kind == LogicElementKind.Battery);
        LogicFixtureElement toggle = fixture.Elements.Single(element =>
            element.Kind == LogicElementKind.Switch);
        LogicEditCommand[] edits = fixture.Elements.Select(element => new LogicEditCommand(
            fixture.PlayerIndex,
            element.Kind,
            element.Cell,
            element.TargetValue,
            element.TargetStructuredData,
            element.TargetLastOutput,
            element.TargetSignLines,
            element.TargetSignUrl,
            DuplicateCount: 2)).ToArray();
        return new LogicReplayPlan(
            new LogicEditCommand(
                fixture.PlayerIndex,
                LogicElementKind.Battery,
                battery.Cell,
                15,
                ForgeAuthoritativeState: true,
                ExpectRejected: true),
            new LogicEditCommand(
                fixture.PlayerIndex,
                LogicElementKind.Battery,
                fixture.FarBatteryCell,
                14,
                ExpectRejected: true),
            new LogicInteractCommand(
                fixture.PlayerIndex,
                toggle.Cell,
                TargetPlayerIndex: observerPlayerIndex,
                ExpectRejected: true),
            new LogicEditCommand(
                observerPlayerIndex,
                LogicElementKind.Battery,
                battery.Cell,
                14,
                ExpectRejected: true),
            edits,
            new LogicInteractCommand(
                fixture.PlayerIndex,
                toggle.Cell,
                DuplicateCount: 2),
            new RunLogicRevisionBurstCommand(
                battery.Cell,
                PersistentVoltageCell: fixture.PersistentVoltageCell),
            new RunMovingBlockLifecycleBurstCommand(fixture.PistonPushCell),
            new RunPistonLifecycleCommand(
                fixture.Elements.Single(element => element.Kind == LogicElementKind.Piston).Cell));
    }

    private static async Task VerifyControlAuthorityAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int actorPlayerIndex,
        CancellationToken cancellationToken) {
        TerrainCellCoordinate dummy = new(0, 64, 0);
        await runner.RunAsync(
            "verify-control-authority",
            "typed authority rejection",
            async token => {
                await RequireRejectionAsync(
                    () => cluster.Clients[0].SendAsync<PrepareLogicFixtureCommand, LogicFixture>(
                        ControlCommandNames.PrepareLogicFixture,
                        new PrepareLogicFixtureCommand(actorPlayerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<LogicEditCommand, ActionReceipt>(
                        ControlCommandNames.LogicEdit,
                        new LogicEditCommand(actorPlayerIndex, LogicElementKind.Battery, dummy),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunLogicRevisionBurstCommand, LogicRevisionBurstResult>(
                        ControlCommandNames.RunLogicRevisionBurst,
                        new RunLogicRevisionBurstCommand(dummy),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunMovingBlockLifecycleBurstCommand, MovingBlockLifecycleBurstResult>(
                        ControlCommandNames.RunMovingBlockLifecycleBurst,
                        new RunMovingBlockLifecycleBurstCommand(dummy),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunPistonLifecycleCommand, PistonLifecycleResult>(
                        ControlCommandNames.RunPistonLifecycle,
                        new RunPistonLifecycleCommand(dummy),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ActionReceipt> SendLogicEditAsync(
        ScenarioRunner runner,
        GameNodeProcess client,
        string stepName,
        LogicEditCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        ActionReceipt receipt = await runner.RunAsync(
            stepName,
            ControlCommandNames.LogicEdit,
            token => client.SendAsync<LogicEditCommand, ActionReceipt>(
                ControlCommandNames.LogicEdit,
                command,
                timeout: timeout,
                cancellationToken: token),
            value => $"actionId={value.ActionId}; code={value.Code}",
            cancellationToken).ConfigureAwait(false);
        EnsureAccepted(receipt);
        return receipt;
    }

    private static async Task<ActionReceipt> SendLogicInteractAsync(
        ScenarioRunner runner,
        GameNodeProcess client,
        string stepName,
        LogicInteractCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        ActionReceipt receipt = await runner.RunAsync(
            stepName,
            ControlCommandNames.LogicInteract,
            token => client.SendAsync<LogicInteractCommand, ActionReceipt>(
                ControlCommandNames.LogicInteract,
                command,
                timeout: timeout,
                cancellationToken: token),
            value => $"actionId={value.ActionId}; code={value.Code}",
            cancellationToken).ConfigureAwait(false);
        EnsureAccepted(receipt);
        return receipt;
    }

    private static async Task<StateSnapshotV2[]> WaitForLogicConvergenceAsync(
        IReadOnlyList<GameNodeProcess> nodes,
        TerrainCellCoordinate[] logicCells,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool compareRevisions,
        Func<StateSnapshotV2, bool>? serverPredicate = null) {
        if (nodes.Count == 0
            || !string.Equals(nodes[0].Role, "server", StringComparison.Ordinal)) {
            throw new ArgumentException("Logic convergence requires the server as the first node.", nameof(nodes));
        }
        StateSnapshotV2 expected = await WaitForLogicSnapshotAsync(
            nodes[0],
            logicCells,
            serverPredicate ?? (_ => true),
            timeout,
            cancellationToken).ConfigureAwait(false);
        SnapshotV2LogicComparisonOptions comparison = new(CompareRevisions: compareRevisions);
        SnapshotV2TerrainComparisonOptions terrainComparison = new(CompareRevision: true);
        StateSnapshotV2[] clients = await Task.WhenAll(nodes.Skip(1).Select(node =>
            WaitForLogicSnapshotAsync(
                node,
                logicCells,
                snapshot => SnapshotV2Comparer.CompareLogic(expected, snapshot, comparison).Equal
                    && SnapshotV2Comparer.CompareTerrainCells(
                        expected,
                        snapshot,
                        terrainComparison).Equal,
                timeout,
                cancellationToken))).ConfigureAwait(false);
        return [expected, .. clients];
    }

    private static async Task<StateSnapshotV2> WaitForLogicSnapshotAsync(
        GameNodeProcess node,
        TerrainCellCoordinate[] logicCells,
        Func<StateSnapshotV2, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureLogicAsync(node, logicCells, cancellationToken)
                .ConfigureAwait(false);
            if (predicate(last)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose the expected logic state. Last={FormatLogic(last)}.");
    }

    private static Task<StateSnapshotV2> CaptureLogicAsync(
        GameNodeProcess node,
        TerrainCellCoordinate[] logicCells,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Terrain, SnapshotDomain.Logic],
                TerrainCells: logicCells,
                LogicCells: logicCells),
            cancellationToken: cancellationToken);

    private static async Task RequireClientRejectionMetricsAsync(
        IReadOnlyList<GameNodeProcess> clients,
        TerrainCellCoordinate[] logicCells,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        await Task.WhenAll(clients.Select(client => WaitForLogicSnapshotAsync(
            client,
            logicCells,
            snapshot => snapshot.LogicRevisions is {
                IgnoredStaleStates: > 0,
                IgnoredLifecycleStates: > 0,
                MovingTombstones: > 0
            },
            timeout,
            cancellationToken))).ConfigureAwait(false);
    }

    private static bool FixtureTargetsApplied(
        StateSnapshotV2 snapshot,
        LogicFixture fixture) =>
        fixture.Elements.All(element => ElementTargetApplied(snapshot, element));

    private static bool ElementTargetApplied(
        StateSnapshotV2 snapshot,
        LogicFixtureElement element) {
        LogicElementStateSnapshotV2 state = FindLogic(snapshot, element.Cell);
        if (state.Revision <= 0) {
            return false;
        }
        return element.Kind switch {
                LogicElementKind.Battery =>
                    state.State == $"voltage={element.TargetValue}",
                LogicElementKind.Button =>
                    state.State == $"voltage={element.TargetValue}",
                LogicElementKind.Switch =>
                    state.State.StartsWith($"voltage={element.TargetValue};", StringComparison.Ordinal),
                LogicElementKind.AdjustableDelayGate =>
                    state.State == $"delay={element.TargetValue}",
                LogicElementKind.Piston => state.Data == element.TargetValue,
                LogicElementKind.MemoryBank =>
                    state.State.Contains($"data={element.TargetStructuredData}", StringComparison.Ordinal)
                    && state.State.EndsWith(
                        $"lastOutput={element.TargetLastOutput}",
                        StringComparison.Ordinal),
                LogicElementKind.TruthTable =>
                    state.State.Contains($"data={element.TargetStructuredData}", StringComparison.Ordinal),
                LogicElementKind.Sign =>
                    state.State.Contains(
                        $"lines={string.Join('|', element.TargetSignLines ?? [])}",
                        StringComparison.Ordinal)
                    && state.State.EndsWith($"url={element.TargetSignUrl}", StringComparison.Ordinal),
                LogicElementKind.Dispenser =>
                    state.State.Contains("mode=Shoot", StringComparison.Ordinal)
                    && state.State.Contains("acceptsDrops=True", StringComparison.Ordinal),
                _ => false
            };
    }

    private static bool PersistedLogicMatches(
        StateSnapshotV2 snapshot,
        LogicFixture fixture,
        LogicRevisionBurstResult revisionResult) {
        LogicFixtureElement battery = fixture.Elements.Single(element =>
            element.Kind == LogicElementKind.Battery);
        LogicFixture persistedTargets = fixture with {
            Elements = fixture.Elements.Select(element =>
                element.Kind == LogicElementKind.Battery
                    ? element with { TargetValue = revisionResult.FinalVoltage }
                    : element.Kind == LogicElementKind.Switch
                        ? element with { TargetValue = 5 }
                        : element).ToArray()
        };
        LogicElementStateSnapshotV2 switchState = FindLogic(
            snapshot,
            fixture.Elements.Single(element => element.Kind == LogicElementKind.Switch).Cell);
        return FixtureTargetsAppliedIgnoringRevision(snapshot, persistedTargets)
            && switchState.Data == fixture.ToggledSwitchData
            && FindLogic(snapshot, battery.Cell).State == $"voltage={revisionResult.FinalVoltage}"
            && snapshot.PersistentVoltages?.Any(voltage =>
                voltage.X == revisionResult.PersistentVoltageCell.X
                && voltage.Y == revisionResult.PersistentVoltageCell.Y
                && voltage.Z == revisionResult.PersistentVoltageCell.Z
                && Math.Abs(voltage.Voltage - revisionResult.FinalPersistentVoltage) <= 0.00001) == true;
    }

    private static Task<ControlAck> ApplyLogicViewAsync(
        GameNodeProcess node,
        int playerIndex,
        LogicFixture fixture,
        int positionOffset,
        CancellationToken cancellationToken) {
        Vector3State position = new(
            fixture.PlayerPosition.X + positionOffset,
            fixture.PlayerPosition.Y,
            fixture.PlayerPosition.Z + positionOffset);
        return node.SendAsync<ApplyTerrainStreamingViewCommand, ControlAck>(
            ControlCommandNames.ApplyTerrainStreamingView,
            new ApplyTerrainStreamingViewCommand(
                playerIndex,
                position,
                StandingValue: 1,
                VisibilityDistance: 48,
                ContentDistance: 64,
                FreezeBody: true),
            cancellationToken: cancellationToken);
    }

    private static bool FixtureTargetsAppliedIgnoringRevision(
        StateSnapshotV2 snapshot,
        LogicFixture fixture) {
        StateSnapshotV2 normalized = snapshot with {
            LogicStates = snapshot.LogicStates?
                .Select(state => state with { Revision = Math.Max(1, state.Revision) })
                .ToArray()
        };
        return FixtureTargetsApplied(normalized, fixture);
    }

    private static bool BatteryAndVoltageMatch(
        StateSnapshotV2 snapshot,
        LogicRevisionBurstResult result) {
        LogicElementStateSnapshotV2 battery = FindLogic(snapshot, result.BatteryCell);
        return battery.State == $"voltage={result.FinalVoltage}"
            && snapshot.PersistentVoltages?.Any(voltage =>
                voltage.X == result.PersistentVoltageCell.X
                && voltage.Y == result.PersistentVoltageCell.Y
                && voltage.Z == result.PersistentVoltageCell.Z
                && Math.Abs(voltage.Voltage - result.FinalPersistentVoltage) <= 0.00001) == true;
    }

    private static bool MovingReplacementMatches(
        StateSnapshotV2 snapshot,
        MovingBlockLifecycleBurstResult result) {
        MovingBlockStateSnapshotV2[] moving = snapshot.MovingBlocks ?? [];
        bool replacement = moving.Any(state =>
            state.Identity == result.Identity
            && state.Present
            && state.Generation == result.FinalGeneration
            && state.Revision == result.FinalRevision);
        MovingBlockStateSnapshotV2[] concurrent = moving
            .Where(state =>
                state.Identity == result.ConcurrentIdentity
                && !state.HasTag
                && state.Present)
            .ToArray();
        return replacement
            && concurrent.Length == result.ConcurrentInstances
            && concurrent.Select(state => state.InstanceId).Distinct().Count()
                == result.ConcurrentInstances;
    }

    private static bool HasPistonMovingSet(
        StateSnapshotV2 snapshot,
        TerrainCellCoordinate pistonCell) =>
        snapshot.MovingBlocks?.Any(state =>
            state.Identity == "Piston"
            && state.HasTag
            && state.Tag == pistonCell) == true;

    private static LogicElementStateSnapshotV2 FindLogic(
        StateSnapshotV2 snapshot,
        TerrainCellCoordinate cell) =>
        snapshot.LogicStates?.SingleOrDefault(state =>
            state.X == cell.X && state.Y == cell.Y && state.Z == cell.Z)
        ?? new LogicElementStateSnapshotV2(
            cell.X, cell.Y, cell.Z, "missing", 0, 0, 0, "missing", -1);

    private static void ValidateFixture(LogicFixture fixture, int actorPlayerIndex) {
        LogicElementKind[] expectedKinds = Enum.GetValues<LogicElementKind>();
        if (fixture.PlayerIndex != actorPlayerIndex
            || fixture.PlayerEntityId <= 0
            || !string.Equals(fixture.Alias, "primary", StringComparison.Ordinal)
            || fixture.Elements.Length != expectedKinds.Length
            || fixture.Elements.Select(element => element.Kind).Distinct().Count()
                != expectedKinds.Length
            || expectedKinds.Any(kind => !fixture.Elements.Any(element => element.Kind == kind))
            || fixture.InitialSwitchData == fixture.ToggledSwitchData) {
            throw new InvalidOperationException($"Logic fixture is incomplete: {fixture}.");
        }
    }

    private static void ValidateRevisionResult(LogicRevisionBurstResult result) {
        if (result.StaleLogicRevision >= result.FinalLogicRevision
            || result.StaleElectricityRevision >= result.FinalElectricityRevision
            || result.FinalVoltage is < 0 or > 15
            || !float.IsFinite(result.FinalPersistentVoltage)) {
            throw new InvalidOperationException($"Logic revision burst is invalid: {result}.");
        }
    }

    private static void ValidateMovingResult(MovingBlockLifecycleBurstResult result) {
        if (!result.ReplacementPresent
            || string.IsNullOrWhiteSpace(result.ConcurrentIdentity)
            || result.ConcurrentInstances != 2
            || result.StaleGeneration <= 0
            || result.StaleGeneration >= result.FinalGeneration
            || result.FinalRevision < 0
            || string.IsNullOrWhiteSpace(result.Identity)) {
            throw new InvalidOperationException($"Moving lifecycle burst is invalid: {result}.");
        }
    }

    private static void ValidateReplayPlan(LogicReplayPlan? plan) {
        if (plan == null) {
            return;
        }
        if (!plan.ForgedState.ExpectRejected
            || !plan.ForgedState.ForgeAuthoritativeState
            || !plan.FarEdit.ExpectRejected
            || !plan.CrossPlayerInteract.ExpectRejected
            || !plan.CrossPlayerInteract.TargetPlayerIndex.HasValue
            || !plan.ObserverEdit.ExpectRejected
            || plan.Edits is not { Length: 9 }
            || plan.Edits.Any(edit => edit.ExpectRejected
                || edit.ForgeAuthoritativeState
                || edit.DuplicateCount < 2)
            || plan.Edits.Select(edit => edit.Kind).Distinct().Count() != 9
            || plan.Toggle.ExpectRejected
            || plan.Toggle.TargetPlayerIndex.HasValue
            || plan.Toggle.DuplicateCount < 2
            || !plan.RevisionBurst.SendStaleState
            || !plan.MovingBurst.SendStaleAdd
            || !plan.PistonLifecycle.Stop) {
            throw new InvalidDataException("Logic replay actions are invalid.");
        }
    }

    private static void RequireEquivalentReplayPlan(
        LogicReplayPlan expected,
        LogicReplayPlan actual) {
        string expectedJson = JsonSerializer.Serialize(expected, ControlProtocol.JsonOptions);
        string actualJson = JsonSerializer.Serialize(actual, ControlProtocol.JsonOptions);
        if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal)) {
            throw new InvalidDataException(
                "Logic replay actions do not match the deterministic fixture generated by this run.");
        }
    }

    private static async Task ConfigureNetworkFaultsAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        NetworkFaultProfile baseProfile,
        CancellationToken cancellationToken) {
        GameNodeProcess[] ordered = nodes.ToArray();
        NetworkFaultProfile[] expected = ordered
            .Select((_, index) => baseProfile with { Seed = unchecked(baseProfile.Seed + index) })
            .ToArray();
        NetworkFaultState[] states = await Task.WhenAll(ordered.Select((node, index) =>
            node.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(expected[index]),
                cancellationToken: cancellationToken))).ConfigureAwait(false);
        for (int index = 0; index < states.Length; index++) {
            if (states[index].Profile != expected[index]) {
                throw new InvalidOperationException(
                    $"Node '{ordered[index].Role}' did not apply its deterministic fault profile.");
            }
        }
    }

    private static async Task<string> SaveStopAndReloadAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        TimeSpan stepTimeout,
        CancellationToken cancellationToken) {
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
                    throw new InvalidOperationException("Server declined to save the logic world.");
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
                    "Server retained client sessions before reload.",
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
            cancellationToken).ConfigureAwait(false);

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
                    "Saved logic world did not become playable.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return savedWorldDirectory;
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        TerrainCellCoordinate[] logicCells,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            if (logicCells.Length > 0) {
                try {
                    GetSnapshotV2Command command = new(
                        [SnapshotDomain.Network, SnapshotDomain.Logic, SnapshotDomain.Events],
                        EventLimit: AutomationProtocol.MaximumEventQueryCount,
                        LogicCells: logicCells);
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
                    // Preserve the original scenario failure.
                }
            }
            try {
                events[$"{node.Role}-final"] = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                    ControlCommandNames.GetEvents,
                    new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // A dead or disconnected node can still contribute retained logs.
            }
        }
    }

    private static async Task<ControlCommandException> RequireRejectionAsync<T>(
        Func<Task<T>> action,
        string code) {
        try {
            _ = await action().ConfigureAwait(false);
        }
        catch (ControlCommandException exception) {
            if (!string.Equals(exception.Code, code, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    $"Expected rejection '{code}', got '{exception.Code}'.",
                    exception);
            }
            return exception;
        }
        throw new InvalidOperationException(
            $"Expected command rejection '{code}', but the command succeeded.");
    }

    private static ScenarioActionRecordV2 ActionRecord<TCommand>(
        string role,
        string command,
        TCommand payload,
        ActionReceipt receipt) =>
        new(
            role,
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            receipt);

    private static TCommand DeserializeAction<TCommand>(ScenarioActionRecordV2 record) =>
        record.Payload.Deserialize<TCommand>(ControlProtocol.JsonOptions)
        ?? throw new InvalidDataException(
            $"Replay action '{record.Receipt.ActionId}' has an invalid {typeof(TCommand).Name} payload.");

    private static void EnsureAccepted(ActionReceipt receipt) {
        if (!receipt.Accepted) {
            throw new InvalidOperationException(
                $"Action '{receipt.ActionId}' was rejected: {receipt.Code}.");
        }
    }

    private static void EnsureEqual(SnapshotV2Diff diff, string message) {
        if (diff.Equal) {
            return;
        }
        string detail = string.Join(
            " | ",
            diff.Differences.Take(8).Select(item =>
                $"{item.Path}: expected={item.Expected}; actual={item.Actual}"));
        throw new InvalidOperationException($"{message}: {detail}");
    }

    private static void AddSnapshots(
        Dictionary<string, StateSnapshotV2> destination,
        string suffix,
        IReadOnlyList<StateSnapshotV2> values,
        params GameNodeProcess[] nodes) {
        for (int index = 0; index < values.Count; index++) {
            destination[$"{nodes[index].Role}-{suffix}"] = values[index];
        }
    }

    private static string FormatLogic(StateSnapshotV2? snapshot) =>
        snapshot?.LogicStates == null
            ? "<none>"
            : string.Join(" | ", snapshot.LogicStates.Select(state =>
                $"({state.X},{state.Y},{state.Z}) {state.Kind} {state.State} r{state.Revision}"));

    private static bool IsFaultProfileEnabled(NetworkFaultProfile profile) =>
        profile.BaseLatencyMilliseconds > 0
        || profile.JitterMilliseconds > 0
        || profile.LossRate > 0
        || profile.DuplicationRate > 0
        || profile.ReorderRate > 0
        || profile.BandwidthBytesPerSecond > 0;

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
}
