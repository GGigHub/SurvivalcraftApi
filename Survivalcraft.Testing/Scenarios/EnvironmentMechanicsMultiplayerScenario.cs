using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record EnvironmentMechanicsReplayPlan(
    EnvironmentRequestChangeCommand Time,
    EnvironmentRequestChangeCommand Precipitation,
    EnvironmentRequestChangeCommand Fog,
    EnvironmentRequestChangeCommand Lightning,
    PlayerSleepCommand Sleep,
    RunEnvironmentRevisionBurstCommand EnvironmentRevisionBurst,
    RunMechanicsRevisionBurstCommand MechanicsRevisionBurst,
    PlayerSleepCommand Wake);

public sealed record EnvironmentMechanicsMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    EnvironmentMechanicsReplayPlan? ReplayPlan = null);

public sealed record EnvironmentMechanicsMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class EnvironmentMechanicsMultiplayerScenarioException : Exception {
    public EnvironmentMechanicsMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class EnvironmentMechanicsMultiplayerScenario {
    public const string ScenarioName = "environment-mechanics-multiplayer";
    public const int ScenarioVersion = 1;

    private static readonly string[] NodeRoles = [
        "server",
        "client-a",
        "client-b",
        "client-c"
    ];

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-cluster", "process.start(server, client-a, client-b, client-c, relay?)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("require-v4-capabilities", ControlCommandNames.Describe),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("start-transport-host", "network.startHost"),
        new("connect-initial-clients", "network.connect(client-a, client-b)"),
        new("verify-control-authority", "typed authority rejection"),
        new("set-environment-fixture", ControlCommandNames.SetEnvironmentFixture),
        new("prepare-mechanics-fixture", ControlCommandNames.PrepareMechanicsFixture),
        new("verify-production-authority", "forged environment/sleep/fire packets"),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("change-time", ControlCommandNames.EnvironmentRequestChange),
        new("start-precipitation", ControlCommandNames.EnvironmentRequestChange),
        new("start-fog", ControlCommandNames.EnvironmentRequestChange),
        new("strike-lightning", ControlCommandNames.EnvironmentRequestChange),
        new("sleep-player", ControlCommandNames.PlayerSleep),
        new("run-environment-revision-burst", ControlCommandNames.RunEnvironmentRevisionBurst),
        new("run-mechanics-revision-burst", ControlCommandNames.RunMechanicsRevisionBurst),
        new("wake-player", ControlCommandNames.PlayerSleep),
        new("verify-convergence", ControlCommandNames.GetSnapshotV2),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("connect-late-client", "network.connect(client-c)"),
        new("verify-late-join", ControlCommandNames.GetSnapshotV2),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("reconnect-actor", "network.connect(client-a)"),
        new("verify-reconnect", ControlCommandNames.GetSnapshotV2),
        new("prepare-persistence", "typed environment/mechanics fixture"),
        new("save-and-stop-world", ControlCommandNames.SaveWorld),
        new("reload-saved-world", ControlCommandNames.ReloadWorld),
        new("restart-transport-host", "network.startHost"),
        new("reconnect-persisted-nodes", "network.connect(client-a, client-c)"),
        new("verify-persistence", ControlCommandNames.GetSnapshotV2)
    ];

    private sealed record StateExpectation(
        double TimeOfDayOffset,
        bool PrecipitationStarted,
        bool FogStarted,
        bool Sleeping,
        bool OnFire,
        float? TimeOfYear = null,
        bool RequireLightning = false,
        long? WeatherRevision = null,
        long? FireRevision = null,
        string? GameMode = null);

    public static NetworkFaultProfile CreateDefaultNetworkFaults(int seed) => new(
        Seed: unchecked(seed + 1201),
        BaseLatencyMilliseconds: 18,
        JitterMilliseconds: 4,
        DuplicationRate: 1,
        ReorderRate: 0.5,
        ReorderDelayMilliseconds: 18);

    public static EnvironmentMechanicsReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        (string Role, string Command)[] expected = [
            ("client-a", ControlCommandNames.EnvironmentRequestChange),
            ("client-a", ControlCommandNames.EnvironmentRequestChange),
            ("client-a", ControlCommandNames.EnvironmentRequestChange),
            ("client-a", ControlCommandNames.EnvironmentRequestChange),
            ("client-a", ControlCommandNames.PlayerSleep),
            ("server", ControlCommandNames.RunEnvironmentRevisionBurst),
            ("server", ControlCommandNames.RunMechanicsRevisionBurst),
            ("client-a", ControlCommandNames.PlayerSleep)
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
        EnvironmentMechanicsReplayPlan plan = new(
            DeserializeAction<EnvironmentRequestChangeCommand>(records[0]),
            DeserializeAction<EnvironmentRequestChangeCommand>(records[1]),
            DeserializeAction<EnvironmentRequestChangeCommand>(records[2]),
            DeserializeAction<EnvironmentRequestChangeCommand>(records[3]),
            DeserializeAction<PlayerSleepCommand>(records[4]),
            DeserializeAction<RunEnvironmentRevisionBurstCommand>(records[5]),
            DeserializeAction<RunMechanicsRevisionBurstCommand>(records[6]),
            DeserializeAction<PlayerSleepCommand>(records[7]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<EnvironmentMechanicsMultiplayerScenarioResult> RunAsync(
        EnvironmentMechanicsMultiplayerScenarioOptions options,
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
                "Environment/mechanics resilience requires a non-empty fault profile.",
                nameof(options.NetworkFaults));
        }

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(10);
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
        MechanicsFixture? mechanicsFixture = null;
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
            RequiredAutomationCapabilities required = new(
                SnapshotSchemaVersion: AutomationProtocol.CurrentSnapshotSchemaVersion,
                Commands: [
                    ControlCommandNames.Describe,
                    ControlCommandNames.GetSnapshotV2,
                    ControlCommandNames.GetEvents,
                    ControlCommandNames.Disconnect,
                    ControlCommandNames.SaveWorld,
                    ControlCommandNames.ReloadWorld,
                    ControlCommandNames.SetNetworkFaults,
                    ControlCommandNames.SetEnvironmentFixture,
                    ControlCommandNames.EnvironmentRequestChange,
                    ControlCommandNames.RunEnvironmentRevisionBurst,
                    ControlCommandNames.PrepareMechanicsFixture,
                    ControlCommandNames.SetPlayerFireFixture,
                    ControlCommandNames.PlayerSleep,
                    ControlCommandNames.PlayerIgnite,
                    ControlCommandNames.RunMechanicsRevisionBurst
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Environment,
                    AutomationCommandDomain.Mechanics
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                required,
                capabilities,
                $"EnvironmentMechanics-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            int initialActorPlayerIndex = actorPlayerIndex;
            GameNodeProcess actorClient = cluster.Clients[0];
            GameNodeProcess observerClient = cluster.Clients[1];
            GameNodeProcess lateClient = cluster.Clients[2];
            GameNodeProcess[] initialNodes = [cluster.Server, actorClient, observerClient];
            NodeStatus observerStatus = await observerClient.GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);
            int observerPlayerIndex = observerStatus.PlayerIndex;
            if (observerPlayerIndex < 0 || observerPlayerIndex == actorPlayerIndex) {
                throw new InvalidOperationException(
                    $"Observer player identity is invalid: {observerPlayerIndex}.");
            }

            await VerifyControlAuthorityRejectionsAsync(
                cluster,
                runner,
                actorPlayerIndex,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "environment-mechanics-control-authority-enforcement",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "server-only fixtures and client-only actions reject the wrong node role"));

            SetEnvironmentFixtureResult environmentFixture = await runner.RunAsync(
                "set-environment-fixture",
                ControlCommandNames.SetEnvironmentFixture,
                token => cluster.Server.SendAsync<
                    SetEnvironmentFixtureCommand,
                    SetEnvironmentFixtureResult>(
                    ControlCommandNames.SetEnvironmentFixture,
                    new SetEnvironmentFixtureCommand(
                        TimeOfDayOffset: 0.125,
                        TimeOfYear: 0.375f),
                    cancellationToken: token),
                result => $"offset={result.TimeOfDayOffset:R}; weatherRevision={result.WeatherRevision}",
                scenarioToken).ConfigureAwait(false);
            mechanicsFixture = await runner.RunAsync(
                "prepare-mechanics-fixture",
                ControlCommandNames.PrepareMechanicsFixture,
                token => cluster.Server.SendAsync<PrepareMechanicsFixtureCommand, MechanicsFixture>(
                    ControlCommandNames.PrepareMechanicsFixture,
                    new PrepareMechanicsFixtureCommand(actorPlayerIndex),
                    cancellationToken: token),
                result => $"entityId={result.PlayerEntityId}",
                scenarioToken).ConfigureAwait(false);
            ValidateFixture(mechanicsFixture, actorPlayerIndex);
            StateExpectation baseline = new(
                environmentFixture.TimeOfDayOffset,
                false,
                false,
                false,
                false,
                TimeOfYear: environmentFixture.TimeOfYear);
            Dictionary<string, StateSnapshotV2> baselineSnapshots = await WaitForStateOnAllAsync(
                initialNodes,
                mechanicsFixture.PlayerEntityId,
                actorPlayerIndex,
                baseline,
                requireStaleEvidence: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, baselineSnapshots, "baseline");

            await VerifyProductionAuthorityRejectionsAsync(
                cluster,
                runner,
                observerPlayerIndex,
                mechanicsFixture,
                baseline,
                snapshots,
                events,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "environment-sleep-fire-production-authority",
                "passed",
                [cluster.Server.Role, observerClient.Role],
                "non-creative time/weather requests were rejected and healed optimistic client state; forged environment/fire state and cross-player sleep request could not mutate server state"));

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            EnvironmentMechanicsReplayPlan plan = options.ReplayPlan
                ?? CreateDefaultPlan(actorPlayerIndex, mechanicsFixture.PlayerEntityId);
            ValidatePlanAgainstFixture(plan, actorPlayerIndex, mechanicsFixture.PlayerEntityId);

            ActionReceipt timeReceipt = await SendActionAsync(
                runner,
                "change-time",
                actorClient,
                ControlCommandNames.EnvironmentRequestChange,
                plan.Time,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.EnvironmentRequestChange, plan.Time, timeReceipt));
            events["client-a-time"] = await WaitForAppliedEventAsync(
                actorClient, timeReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            ActionReceipt precipitationReceipt = await SendActionAsync(
                runner,
                "start-precipitation",
                actorClient,
                ControlCommandNames.EnvironmentRequestChange,
                plan.Precipitation,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(
                actorClient.Role,
                ControlCommandNames.EnvironmentRequestChange,
                plan.Precipitation,
                precipitationReceipt));
            events["client-a-precipitation"] = await WaitForAppliedEventAsync(
                actorClient, precipitationReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            ActionReceipt fogReceipt = await SendActionAsync(
                runner,
                "start-fog",
                actorClient,
                ControlCommandNames.EnvironmentRequestChange,
                plan.Fog,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.EnvironmentRequestChange, plan.Fog, fogReceipt));
            events["client-a-fog"] = await WaitForAppliedEventAsync(
                actorClient, fogReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            ActionReceipt lightningReceipt = await SendActionAsync(
                runner,
                "strike-lightning",
                actorClient,
                ControlCommandNames.EnvironmentRequestChange,
                plan.Lightning,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(
                actorClient.Role,
                ControlCommandNames.EnvironmentRequestChange,
                plan.Lightning,
                lightningReceipt));
            events["client-a-lightning"] = await WaitForAppliedEventAsync(
                actorClient, lightningReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            ActionReceipt sleepReceipt = await SendActionAsync(
                runner,
                "sleep-player",
                actorClient,
                ControlCommandNames.PlayerSleep,
                plan.Sleep,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.PlayerSleep, plan.Sleep, sleepReceipt));
            events["client-a-sleep"] = await WaitForAppliedEventAsync(
                actorClient, sleepReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            StateExpectation activeExpectation = new(
                plan.Time.TimeOfDayOffset!.Value,
                true,
                true,
                true,
                false,
                TimeOfYear: environmentFixture.TimeOfYear,
                RequireLightning: true);
            Dictionary<string, StateSnapshotV2> activeSnapshots = await WaitForStateOnAllAsync(
                initialNodes,
                mechanicsFixture.PlayerEntityId,
                actorPlayerIndex,
                activeExpectation,
                requireStaleEvidence: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, activeSnapshots, "environment-sleep-active");

            EnvironmentRevisionBurstResult environmentBurst = await runner.RunAsync(
                "run-environment-revision-burst",
                ControlCommandNames.RunEnvironmentRevisionBurst,
                token => cluster.Server.SendAsync<
                    RunEnvironmentRevisionBurstCommand,
                    EnvironmentRevisionBurstResult>(
                    ControlCommandNames.RunEnvironmentRevisionBurst,
                    plan.EnvironmentRevisionBurst,
                    cancellationToken: token),
                result => $"revision={result.FinalRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(environmentBurst.Receipt);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.RunEnvironmentRevisionBurst,
                plan.EnvironmentRevisionBurst,
                environmentBurst.Receipt));
            events["server-environment-revision-burst"] = await WaitForAppliedEventAsync(
                cluster.Server, environmentBurst.Receipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            MechanicsRevisionBurstResult mechanicsBurst = await runner.RunAsync(
                "run-mechanics-revision-burst",
                ControlCommandNames.RunMechanicsRevisionBurst,
                token => cluster.Server.SendAsync<
                    RunMechanicsRevisionBurstCommand,
                    MechanicsRevisionBurstResult>(
                    ControlCommandNames.RunMechanicsRevisionBurst,
                    plan.MechanicsRevisionBurst,
                    cancellationToken: token),
                result => $"revision={result.FinalRevision}; duration={result.FinalDuration:R}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(mechanicsBurst.Receipt);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.RunMechanicsRevisionBurst,
                plan.MechanicsRevisionBurst,
                mechanicsBurst.Receipt));
            events["server-mechanics-revision-burst"] = await WaitForAppliedEventAsync(
                cluster.Server, mechanicsBurst.Receipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            ActionReceipt wakeReceipt = await SendActionAsync(
                runner,
                "wake-player",
                actorClient,
                ControlCommandNames.PlayerSleep,
                plan.Wake,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.PlayerSleep, plan.Wake, wakeReceipt));
            events["client-a-wake"] = await WaitForAppliedEventAsync(
                actorClient, wakeReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            StateExpectation finalExpectation = activeExpectation with {
                PrecipitationStarted = environmentBurst.FinalPrecipitationStarted,
                Sleeping = false,
                OnFire = true,
                WeatherRevision = environmentBurst.FinalRevision,
                FireRevision = mechanicsBurst.FinalRevision
            };
            Dictionary<string, StateSnapshotV2> finalSnapshots = await WaitForStateOnAllAsync(
                initialNodes,
                mechanicsFixture.PlayerEntityId,
                actorPlayerIndex,
                finalExpectation,
                requireStaleEvidence: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, finalSnapshots, "fault-convergence");
            EnsureRevisionConvergence(
                finalSnapshots.Values.ToArray(),
                mechanicsFixture.PlayerEntityId,
                actorPlayerIndex);
            assertions.Add(new ScenarioAssertionRecordV2(
                "environment-mechanics-duplicate-reorder-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"weatherRevision={environmentBurst.FinalRevision}; fireRevision={mechanicsBurst.FinalRevision}"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(),
                    token),
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "connect-late-client",
                "network.connect(client-c)",
                token => MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                    cluster,
                    lateClient,
                    "LearnerC",
                    stepTimeout,
                    token),
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 lateSnapshot = await WaitForStateAsync(
                lateClient,
                mechanicsFixture.PlayerEntityId,
                actorPlayerIndex,
                finalExpectation with { RequireLightning = false },
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["client-c-late-join"] = lateSnapshot;
            assertions.Add(new ScenarioAssertionRecordV2(
                "environment-mechanics-late-join-baseline",
                "passed",
                [cluster.Server.Role, lateClient.Role],
                "late client received current environment, awake state and remaining fire state"));

            await runner.RunAsync(
                "disconnect-actor",
                ControlCommandNames.Disconnect,
                async token => {
                    _ = await actorClient.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await actorClient.WaitForStatusAsync(
                        status => !status.ProjectLoaded && !status.IsClientRunning,
                        stepTimeout,
                        "Actor did not disconnect.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "reconnect-actor",
                "network.connect(client-a)",
                token => MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                    cluster,
                    actorClient,
                    "LearnerA",
                    stepTimeout,
                    token),
                scenarioToken).ConfigureAwait(false);
            NodeStatus reconnectedActor = await actorClient.GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);
            EnsureActorIdentity(reconnectedActor, initialActorPlayerIndex, "reconnect");
            StateSnapshotV2 reconnectSnapshot = await WaitForStateAsync(
                actorClient,
                mechanicsFixture.PlayerEntityId,
                actorPlayerIndex,
                finalExpectation with {
                    RequireLightning = false,
                    FireRevision = null
                },
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["client-a-reconnect"] = reconnectSnapshot;
            int reconnectedActorEntityId = RequirePlayerEntityId(
                reconnectSnapshot,
                actorPlayerIndex);
            metrics["actor.initialEntityId"] = mechanicsFixture.PlayerEntityId
                .ToString(CultureInfo.InvariantCulture);
            metrics["actor.reconnectedEntityId"] = reconnectedActorEntityId
                .ToString(CultureInfo.InvariantCulture);
            assertions.Add(new ScenarioAssertionRecordV2(
                "environment-mechanics-reconnect-baseline",
                "passed",
                [cluster.Server.Role, actorClient.Role],
                "same player identity mapped to a fresh entity generation with authoritative awake/fire state"));

            SetEnvironmentFixtureResult persistenceEnvironment = await runner.RunAsync(
                "prepare-persistence",
                "typed environment/mechanics fixture",
                async token => {
                    SetEnvironmentFixtureResult result = await cluster.Server.SendAsync<
                        SetEnvironmentFixtureCommand,
                        SetEnvironmentFixtureResult>(
                        ControlCommandNames.SetEnvironmentFixture,
                        new SetEnvironmentFixtureCommand(
                            TimeOfDayOffset: finalExpectation.TimeOfDayOffset,
                            TimeOfYear: 0.625f,
                            PrecipitationStarted: false,
                            FogStarted: false),
                        cancellationToken: token).ConfigureAwait(false);
                    MechanicsFixture prepared = await cluster.Server.SendAsync<
                        PrepareMechanicsFixtureCommand,
                        MechanicsFixture>(
                        ControlCommandNames.PrepareMechanicsFixture,
                        new PrepareMechanicsFixtureCommand(actorPlayerIndex),
                        cancellationToken: token).ConfigureAwait(false);
                    ValidateFixture(prepared, actorPlayerIndex);
                    if (prepared.PlayerEntityId != reconnectedActorEntityId) {
                        throw new InvalidOperationException(
                            $"Mechanics fixture resolved entity {prepared.PlayerEntityId}, expected reconnect generation {reconnectedActorEntityId}.");
                    }
                    ActionReceipt sleepReceipt = await actorClient.SendAsync<
                        PlayerSleepCommand,
                        ActionReceipt>(
                        ControlCommandNames.PlayerSleep,
                        new PlayerSleepCommand(actorPlayerIndex, Sleep: true),
                        cancellationToken: token).ConfigureAwait(false);
                    EnsureAccepted(sleepReceipt);
                    _ = await cluster.Server.SendAsync<
                        SetPlayerFireFixtureCommand,
                        SetPlayerFireFixtureResult>(
                        ControlCommandNames.SetPlayerFireFixture,
                        new SetPlayerFireFixtureCommand(
                            reconnectedActorEntityId,
                            Duration: 120f),
                        cancellationToken: token).ConfigureAwait(false);
                    return result;
                },
                result => $"offset={result.TimeOfDayOffset:R}; timeOfYear={result.TimeOfYear:R}",
                scenarioToken).ConfigureAwait(false);
            StateExpectation persistenceExpectation = new(
                persistenceEnvironment.TimeOfDayOffset,
                false,
                false,
                true,
                true,
                TimeOfYear: persistenceEnvironment.TimeOfYear);
            GameNodeProcess[] preSaveNodes = [
                cluster.Server,
                actorClient,
                observerClient,
                lateClient
            ];
            Dictionary<string, StateSnapshotV2> preSaveSnapshots = await WaitForStateOnAllAsync(
                preSaveNodes,
                reconnectedActorEntityId,
                actorPlayerIndex,
                persistenceExpectation,
                requireStaleEvidence: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, preSaveSnapshots, "persistence-baseline");

            string savedWorldDirectory = await SaveStopAndReloadAsync(
                cluster,
                runner,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            string? reloadRoom = await runner.RunAsync(
                "restart-transport-host",
                "network.startHost",
                token => cluster.StartHostAsync(stepTimeout, token),
                room => room ?? "direct",
                scenarioToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(reloadRoom)) {
                metrics["relay.reloadRoomCode"] = reloadRoom;
            }
            await runner.RunAsync(
                "reconnect-persisted-nodes",
                "network.connect(client-a, client-c)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        actorClient,
                        "LearnerA",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        lateClient,
                        "LearnerC",
                        stepTimeout,
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            NodeStatus persistedActor = await actorClient.GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);
            EnsureActorIdentity(persistedActor, initialActorPlayerIndex, "save/reload");
            GameNodeProcess[] persistenceNodes = [cluster.Server, actorClient, lateClient];
            Dictionary<string, StateSnapshotV2> persistedSnapshots = await WaitForStateOnAllAsync(
                persistenceNodes,
                reconnectedActorEntityId,
                actorPlayerIndex,
                persistenceExpectation,
                requireStaleEvidence: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, persistedSnapshots, "after-reload");
            assertions.Add(new ScenarioAssertionRecordV2(
                "environment-mechanics-save-reload-persistence",
                "passed",
                persistenceNodes.Select(node => node.Role).ToArray(),
                $"world={savedWorldDirectory}; offset={persistenceEnvironment.TimeOfDayOffset:R}; sleeping=true; onFire=true"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(
                    cluster,
                    mechanicsFixture,
                    actorPlayerIndex,
                    snapshots,
                    events).ConfigureAwait(false);
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
            metrics["networkFaults"] = FormatFaultProfile(faultProfile);
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
            throw new EnvironmentMechanicsMultiplayerScenarioException(
                $"Environment/mechanics multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new EnvironmentMechanicsMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new EnvironmentMechanicsMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private static EnvironmentMechanicsReplayPlan CreateDefaultPlan(
        int playerIndex,
        int entityId) => new(
        new EnvironmentRequestChangeCommand(
            playerIndex,
            EnvironmentChangeKind.TimeOfDay,
            TimeOfDayOffset: 0.375),
        new EnvironmentRequestChangeCommand(
            playerIndex,
            EnvironmentChangeKind.PrecipitationStart),
        new EnvironmentRequestChangeCommand(
            playerIndex,
            EnvironmentChangeKind.FogStart),
        new EnvironmentRequestChangeCommand(
            playerIndex,
            EnvironmentChangeKind.Lightning),
        new PlayerSleepCommand(playerIndex, Sleep: true),
        new RunEnvironmentRevisionBurstCommand(FinalPrecipitationStarted: true),
        new RunMechanicsRevisionBurstCommand(
            entityId,
            IntermediateDuration: 180f,
            FinalDuration: 120f),
        new PlayerSleepCommand(playerIndex, Sleep: false));

    private static async Task VerifyControlAuthorityRejectionsAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int playerIndex,
        CancellationToken cancellationToken) {
        await runner.RunAsync(
            "verify-control-authority",
            "typed authority rejection",
            async token => {
                await RequireRejectionAsync(
                    () => cluster.Clients[0].SendAsync<
                        SetEnvironmentFixtureCommand,
                        SetEnvironmentFixtureResult>(
                        ControlCommandNames.SetEnvironmentFixture,
                        new SetEnvironmentFixtureCommand(),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<
                        RunMechanicsRevisionBurstCommand,
                        MechanicsRevisionBurstResult>(
                        ControlCommandNames.RunMechanicsRevisionBurst,
                        new RunMechanicsRevisionBurstCommand(1),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<EnvironmentRequestChangeCommand, ActionReceipt>(
                        ControlCommandNames.EnvironmentRequestChange,
                        new EnvironmentRequestChangeCommand(
                            playerIndex,
                            EnvironmentChangeKind.TimeOfDay,
                            TimeOfDayOffset: 0.25),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<PlayerSleepCommand, ActionReceipt>(
                        ControlCommandNames.PlayerSleep,
                        new PlayerSleepCommand(playerIndex, Sleep: true),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyProductionAuthorityRejectionsAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int observerPlayerIndex,
        MechanicsFixture fixture,
        StateExpectation baseline,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        GameNodeProcess observer = cluster.Clients[1];
        SetEnvironmentFixtureCommand AuthorityFixture(bool creativeMode) => new(
            TimeOfDayOffset: baseline.TimeOfDayOffset,
            TimeOfYear: baseline.TimeOfYear ?? 0f,
            PrecipitationStarted: baseline.PrecipitationStarted,
            FogStarted: baseline.FogStarted,
            CreativeMode: creativeMode);

        SetEnvironmentFixtureResult survivalFixture = await runner.RunAsync(
            "verify-production-authority",
            ControlCommandNames.SetEnvironmentFixture,
            token => cluster.Server.SendAsync<
                SetEnvironmentFixtureCommand,
                SetEnvironmentFixtureResult>(
                ControlCommandNames.SetEnvironmentFixture,
                AuthorityFixture(creativeMode: false),
                cancellationToken: token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        StateExpectation nonCreativeBaseline = baseline with {
            GameMode = "Survival",
            WeatherRevision = survivalFixture.WeatherRevision
        };
        snapshots["server-survival-authority-fixture"] = await WaitForStateAsync(
            cluster.Server,
            fixture.PlayerEntityId,
            fixture.PlayerIndex,
            nonCreativeBaseline,
            timeout,
            cancellationToken).ConfigureAwait(false);
        ActionReceipt nonCreativeEnvironment = await SendActionAsync(
            runner,
            "verify-production-authority",
            observer,
            ControlCommandNames.EnvironmentRequestChange,
            new EnvironmentRequestChangeCommand(
                observerPlayerIndex,
                EnvironmentChangeKind.TimeOfDay,
                TimeOfDayOffset: baseline.TimeOfDayOffset + 0.25,
                ExpectRejected: true),
            cancellationToken).ConfigureAwait(false);
        events["observer-noncreative-environment"] = await WaitForAppliedEventAsync(
            observer,
            nonCreativeEnvironment.ActionId,
            timeout,
            cancellationToken).ConfigureAwait(false);
        EnvironmentChangeKind rejectedWeatherChange = baseline.PrecipitationStarted
            ? EnvironmentChangeKind.PrecipitationEnd
            : EnvironmentChangeKind.PrecipitationStart;
        ActionReceipt nonCreativeWeather = await SendActionAsync(
            runner,
            "verify-production-authority",
            observer,
            ControlCommandNames.EnvironmentRequestChange,
            new EnvironmentRequestChangeCommand(
                observerPlayerIndex,
                rejectedWeatherChange,
                ExpectRejected: true),
            cancellationToken).ConfigureAwait(false);
        events["observer-noncreative-weather"] = await WaitForAppliedEventAsync(
            observer,
            nonCreativeWeather.ActionId,
            timeout,
            cancellationToken).ConfigureAwait(false);
        snapshots["server-after-noncreative-environment"] = await WaitForStableStateAsync(
            cluster.Server,
            fixture.PlayerEntityId,
            fixture.PlayerIndex,
            nonCreativeBaseline,
            TimeSpan.FromMilliseconds(450),
            timeout,
            cancellationToken).ConfigureAwait(false);
        snapshots["observer-after-noncreative-environment"] = await WaitForStableStateAsync(
            observer,
            fixture.PlayerEntityId,
            fixture.PlayerIndex,
            baseline with { WeatherRevision = survivalFixture.WeatherRevision },
            TimeSpan.FromMilliseconds(450),
            timeout,
            cancellationToken).ConfigureAwait(false);
        _ = await runner.RunAsync(
            "verify-production-authority",
            ControlCommandNames.SetEnvironmentFixture,
            token => cluster.Server.SendAsync<
                SetEnvironmentFixtureCommand,
                SetEnvironmentFixtureResult>(
                ControlCommandNames.SetEnvironmentFixture,
                AuthorityFixture(creativeMode: true),
                cancellationToken: token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        snapshots["server-creative-authority-restored"] = await WaitForStateAsync(
            cluster.Server,
            fixture.PlayerEntityId,
            fixture.PlayerIndex,
            baseline with { GameMode = "Creative" },
            timeout,
            cancellationToken).ConfigureAwait(false);

        ActionReceipt environment = await SendActionAsync(
            runner,
            "verify-production-authority",
            observer,
            ControlCommandNames.EnvironmentRequestChange,
            new EnvironmentRequestChangeCommand(
                observerPlayerIndex,
                EnvironmentChangeKind.TimeOfDay,
                TimeOfDayOffset: baseline.TimeOfDayOffset + 0.75,
                ForgeAuthoritativeState: true,
                ExpectRejected: true),
            cancellationToken).ConfigureAwait(false);
        events["observer-forged-environment"] = await WaitForAppliedEventAsync(
            observer, environment.ActionId, timeout, cancellationToken).ConfigureAwait(false);
        snapshots["server-after-forged-environment"] = await WaitForStableStateAsync(
            cluster.Server,
            fixture.PlayerEntityId,
            fixture.PlayerIndex,
            baseline,
            TimeSpan.FromMilliseconds(450),
            timeout,
            cancellationToken).ConfigureAwait(false);

        ActionReceipt sleep = await SendActionAsync(
            runner,
            "verify-production-authority",
            observer,
            ControlCommandNames.PlayerSleep,
            new PlayerSleepCommand(
                observerPlayerIndex,
                Sleep: true,
                TargetEntityId: fixture.PlayerEntityId,
                ExpectRejected: true),
            cancellationToken).ConfigureAwait(false);
        events["observer-forged-sleep"] = await WaitForAppliedEventAsync(
            observer, sleep.ActionId, timeout, cancellationToken).ConfigureAwait(false);
        snapshots["server-after-forged-sleep"] = await WaitForStableStateAsync(
            cluster.Server,
            fixture.PlayerEntityId,
            fixture.PlayerIndex,
            baseline,
            TimeSpan.FromMilliseconds(450),
            timeout,
            cancellationToken).ConfigureAwait(false);

        ActionReceipt ignite = await SendActionAsync(
            runner,
            "verify-production-authority",
            observer,
            ControlCommandNames.PlayerIgnite,
            new PlayerIgniteCommand(
                observerPlayerIndex,
                fixture.PlayerEntityId,
                ExpectRejected: true),
            cancellationToken).ConfigureAwait(false);
        events["observer-forged-ignite"] = await WaitForAppliedEventAsync(
            observer, ignite.ActionId, timeout, cancellationToken).ConfigureAwait(false);
        snapshots["server-after-forged-ignite"] = await WaitForStableStateAsync(
            cluster.Server,
            fixture.PlayerEntityId,
            fixture.PlayerIndex,
            baseline,
            TimeSpan.FromMilliseconds(450),
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ActionReceipt> SendActionAsync<TCommand>(
        ScenarioRunner runner,
        string step,
        GameNodeProcess node,
        string commandName,
        TCommand command,
        CancellationToken cancellationToken) {
        ActionReceipt receipt = await runner.RunAsync(
            step,
            commandName,
            token => node.SendAsync<TCommand, ActionReceipt>(
                commandName,
                command,
                cancellationToken: token),
            result => $"actionId={result.ActionId}; code={result.Code}",
            cancellationToken).ConfigureAwait(false);
        EnsureAccepted(receipt);
        return receipt;
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

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForStateOnAllAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        int entityId,
        int playerIndex,
        StateExpectation expectation,
        bool requireStaleEvidence,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Dictionary<string, StateSnapshotV2> result = new(StringComparer.Ordinal);
        foreach (GameNodeProcess node in nodes) {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) {
                throw new TimeoutException("Environment/mechanics convergence exceeded its deadline.");
            }
            result[node.Role] = await WaitForStateAsync(
                node,
                entityId,
                playerIndex,
                expectation,
                remaining,
                cancellationToken).ConfigureAwait(false);
        }
        while (requireStaleEvidence
            && !result.Any(pair =>
                pair.Key.StartsWith("client", StringComparison.Ordinal)
                && pair.Value.StateRevisions?.IgnoredStaleStates > 0)) {
            if (DateTimeOffset.UtcNow >= deadline) {
                throw new TimeoutException(
                    "State converged, but no client observed a stale-state rejection.");
            }
            foreach (GameNodeProcess node in nodes.Where(node =>
                         node.Role.StartsWith("client", StringComparison.Ordinal))) {
                result[node.Role] = await CaptureAsync(
                    node,
                    entityId,
                    playerIndex,
                    cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        return result;
    }

    private static async Task<StateSnapshotV2> WaitForStateAsync(
        GameNodeProcess node,
        int entityId,
        int playerIndex,
        StateExpectation expectation,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureAsync(node, entityId, playerIndex, cancellationToken)
                .ConfigureAwait(false);
            if (Matches(last, entityId, playerIndex, expectation)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge environment/mechanics state. Last={FormatState(last, entityId, playerIndex)}.");
    }

    private static async Task<StateSnapshotV2> WaitForStableStateAsync(
        GameNodeProcess node,
        int entityId,
        int playerIndex,
        StateExpectation expectation,
        TimeSpan minimumStableDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        DateTimeOffset? stableSince = null;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            last = await CaptureAsync(node, entityId, playerIndex, cancellationToken)
                .ConfigureAwait(false);
            if (Matches(last, entityId, playerIndex, expectation)) {
                stableSince ??= DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - stableSince >= minimumStableDuration) {
                    return last;
                }
            }
            else {
                stableSince = null;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not keep state unchanged after a forged request. Last={FormatState(last, entityId, playerIndex)}.");
    }

    private static bool Matches(
        StateSnapshotV2 snapshot,
        int entityId,
        int playerIndex,
        StateExpectation expectation) {
        EnvironmentSnapshotV2? environment = snapshot.Environment;
        MechanicsStateSnapshotV2? mechanics = ResolveMechanicsState(
            snapshot,
            entityId,
            playerIndex);
        return environment != null
            && mechanics is { Exists: true, HasSleep: true, HasOnFire: true }
            && Math.Abs(environment.TimeOfDayOffset - expectation.TimeOfDayOffset) <= 0.001
            && (!expectation.TimeOfYear.HasValue
                || Math.Abs(environment.TimeOfYear - expectation.TimeOfYear.Value) <= 0.001)
            && environment.IsPrecipitationStarted == expectation.PrecipitationStarted
            && environment.IsFogStarted == expectation.FogStarted
            && mechanics.IsSleeping == expectation.Sleeping
            && mechanics.IsOnFire == expectation.OnFire
            && (!expectation.RequireLightning
                || environment.SkyRevision > 0
                    && environment.LastLightningStrikeTime > 0)
            && (!expectation.WeatherRevision.HasValue
                || environment.WeatherRevision == expectation.WeatherRevision.Value)
            && (!expectation.FireRevision.HasValue
                || mechanics.FireRevision == expectation.FireRevision.Value)
            && (expectation.GameMode == null
                || string.Equals(
                    environment.GameMode,
                    expectation.GameMode,
                    StringComparison.Ordinal));
    }

    private static Task<StateSnapshotV2> CaptureAsync(
        GameNodeProcess node,
        int entityId,
        int playerIndex,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [
                    SnapshotDomain.Network,
                    SnapshotDomain.Players,
                    SnapshotDomain.Environment,
                    SnapshotDomain.Mechanics
                ],
                PlayerIndexes: playerIndex < 0 ? [] : [playerIndex],
                EntityIds: null),
            cancellationToken: cancellationToken);

    private static async Task<ActionEventsResult> WaitForAppliedEventAsync(
        GameNodeProcess node,
        string actionId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        ActionEventsResult? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            last = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                ControlCommandNames.GetEvents,
                new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            ActionEventRecord[] matching = last.Events
                .Where(item => item.ActionId == actionId)
                .ToArray();
            if (matching.Any(item => item.Phase == ActionEventPhase.Queued)
                && matching.Any(item => item.Phase == ActionEventPhase.Applied)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not publish applied events for '{actionId}'. Last={last}.");
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
                    throw new InvalidOperationException(
                        "Server declined to save the environment/mechanics world.");
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
                    "Server retained sessions before reload.",
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
                    "Saved environment/mechanics world did not become playable.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return savedWorldDirectory;
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        MechanicsFixture? fixture,
        int actorPlayerIndex,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            try {
                snapshots[$"{node.Role}-final"] = await CaptureAsync(
                    node,
                    fixture?.PlayerEntityId ?? -1,
                    actorPlayerIndex,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Preserve the original scenario failure.
            }
            try {
                events[$"{node.Role}-final"] = await node.SendAsync<
                    GetEventsCommand,
                    ActionEventsResult>(
                    ControlCommandNames.GetEvents,
                    new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // A disconnected node can still contribute retained logs.
            }
        }
    }

    private static void EnsureRevisionConvergence(
        IReadOnlyList<StateSnapshotV2> values,
        int entityId,
        int playerIndex) {
        long weatherRevision = values[0].Environment?.WeatherRevision ?? -1;
        MechanicsStateSnapshotV2 mechanics = ResolveMechanicsState(
            values[0],
            entityId,
            playerIndex)
            ?? throw new InvalidOperationException("Missing mechanics snapshot.");
        foreach (StateSnapshotV2 snapshot in values.Skip(1)) {
            MechanicsStateSnapshotV2 actual = ResolveMechanicsState(
                snapshot,
                entityId,
                playerIndex)
                ?? throw new InvalidOperationException("Missing mechanics snapshot.");
            if (snapshot.Environment?.WeatherRevision != weatherRevision
                || actual.SleepRevision != mechanics.SleepRevision
                || actual.FireRevision != mechanics.FireRevision
                || actual.IsSleeping != mechanics.IsSleeping
                || actual.IsOnFire != mechanics.IsOnFire) {
                throw new InvalidOperationException("Environment/mechanics revisions diverged across nodes.");
            }
        }
    }

    private static void ValidateFixture(MechanicsFixture fixture, int playerIndex) {
        if (fixture.PlayerIndex != playerIndex
            || fixture.PlayerEntityId <= 0
            || fixture.IsSleeping
            || fixture.IsOnFire) {
            throw new InvalidOperationException($"Invalid mechanics fixture: {fixture}.");
        }
    }

    private static void ValidatePlanAgainstFixture(
        EnvironmentMechanicsReplayPlan plan,
        int playerIndex,
        int entityId) {
        ValidateReplayPlan(plan);
        if (plan.Time.PlayerIndex != playerIndex
            || plan.Precipitation.PlayerIndex != playerIndex
            || plan.Fog.PlayerIndex != playerIndex
            || plan.Lightning.PlayerIndex != playerIndex
            || plan.Sleep.PlayerIndex != playerIndex
            || plan.Wake.PlayerIndex != playerIndex
            || plan.MechanicsRevisionBurst.EntityId != entityId) {
            throw new InvalidDataException(
                "Environment/mechanics replay actions do not match the prepared fixture.");
        }
    }

    private static void ValidateReplayPlan(EnvironmentMechanicsReplayPlan? plan) {
        if (plan == null) {
            return;
        }
        if (plan.Time.Change != EnvironmentChangeKind.TimeOfDay
            || !plan.Time.TimeOfDayOffset.HasValue
            || plan.Time.ForgeAuthoritativeState
            || plan.Time.ExpectRejected
            || plan.Precipitation.Change != EnvironmentChangeKind.PrecipitationStart
            || plan.Precipitation.ForgeAuthoritativeState
            || plan.Fog.Change != EnvironmentChangeKind.FogStart
            || plan.Fog.ForgeAuthoritativeState
            || plan.Lightning.Change != EnvironmentChangeKind.Lightning
            || plan.Lightning.ForgeAuthoritativeState
            || !plan.Sleep.Sleep
            || plan.Sleep.ExpectRejected
            || plan.Wake.Sleep
            || plan.Wake.ExpectRejected
            || !plan.EnvironmentRevisionBurst.SendStaleSnapshot
            || !plan.MechanicsRevisionBurst.SendStaleSnapshot
            || plan.MechanicsRevisionBurst.EntityId <= 0) {
            throw new InvalidDataException("Environment/mechanics replay actions are invalid.");
        }
    }

    private static TCommand DeserializeAction<TCommand>(ScenarioActionRecordV2 record) =>
        record.Payload.Deserialize<TCommand>(ControlProtocol.JsonOptions)
        ?? throw new InvalidDataException(
            $"Replay action '{record.Receipt.ActionId}' has an invalid {typeof(TCommand).Name} payload.");

    private static ScenarioActionRecordV2 ActionRecord<TCommand>(
        string role,
        string command,
        TCommand payload,
        ActionReceipt receipt) => new(
        role,
        command,
        JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
        receipt);

    private static void EnsureAccepted(ActionReceipt receipt) {
        if (!receipt.Accepted) {
            throw new InvalidOperationException(
                $"Action '{receipt.ActionId}' was rejected: {receipt.Code}.");
        }
    }

    private static void EnsureActorIdentity(
        NodeStatus status,
        int expectedPlayerIndex,
        string context) {
        if (!status.MainPlayerReady || status.PlayerIndex != expectedPlayerIndex) {
            throw new InvalidOperationException(
                $"Actor identity changed from player {expectedPlayerIndex} to "
                + $"{status.PlayerIndex} after {context}.");
        }
    }

    private static void AddSnapshots(
        Dictionary<string, StateSnapshotV2> destination,
        IReadOnlyDictionary<string, StateSnapshotV2> source,
        string suffix) {
        foreach ((string role, StateSnapshotV2 snapshot) in source) {
            destination[$"{role}-{suffix}"] = snapshot;
        }
    }

    private static MechanicsStateSnapshotV2? ResolveMechanicsState(
        StateSnapshotV2 snapshot,
        int fallbackEntityId,
        int playerIndex) {
        int entityId = snapshot.Players
            .SingleOrDefault(player => player.PlayerIndex == playerIndex)?
            .EntityId
            ?? fallbackEntityId;
        return snapshot.MechanicsStates?
            .SingleOrDefault(state => state.EntityId == entityId);
    }

    private static int RequirePlayerEntityId(StateSnapshotV2 snapshot, int playerIndex) {
        int? entityId = snapshot.Players
            .SingleOrDefault(player => player.PlayerIndex == playerIndex)?
            .EntityId;
        if (entityId is not > 0) {
            throw new InvalidOperationException(
                $"Player {playerIndex} has no active entity in the reconnect snapshot.");
        }
        return entityId.Value;
    }

    private static string FormatState(
        StateSnapshotV2? snapshot,
        int entityId,
        int playerIndex) {
        MechanicsStateSnapshotV2? mechanics = snapshot == null
            ? null
            : ResolveMechanicsState(snapshot, entityId, playerIndex);
        return $"environment={snapshot?.Environment}; mechanics={mechanics}; revisions={snapshot?.StateRevisions}";
    }

    private static string FormatFaultProfile(NetworkFaultProfile profile) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"seed={profile.Seed}; latencyMs={profile.BaseLatencyMilliseconds}; jitterMs={profile.JitterMilliseconds}; loss={profile.LossRate:R}; duplicate={profile.DuplicationRate:R}; reorder={profile.ReorderRate:R}; reorderDelayMs={profile.ReorderDelayMilliseconds}; bandwidth={profile.BandwidthBytesPerSecond}");

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

    private static void TryDeleteCreatedRunDirectory(string runDirectory, string workBase) {
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
