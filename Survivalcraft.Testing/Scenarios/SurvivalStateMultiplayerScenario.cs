using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record SurvivalStateReplayPlan(
    SetCreatureBehaviorStateCommand BehaviorState,
    SetPlayerSurvivalStateCommand PlayerState,
    RequestSkinTransferCommand ActorSkinRequest,
    TriggerCreatureSoundCommand ServerSound,
    TriggerCreatureSoundCommand ClientSound,
    ForgeSurvivalStateCommand AuthorityProbe,
    RunSurvivalStateRevisionBurstCommand RevisionBurst,
    RequestSkinTransferCommand LateJoinSkinRequest);

public sealed record SurvivalStateMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    SurvivalStateReplayPlan? ReplayPlan = null);

public sealed record SurvivalStateMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class SurvivalStateMultiplayerScenarioException : Exception {
    public SurvivalStateMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class SurvivalStateMultiplayerScenario {
    public const string ScenarioName = "survival-state-multiplayer";
    public const int ScenarioVersion = 1;

    private static readonly string[] NodeRoles = [
        "server",
        "client-a",
        "client-b",
        "client-c"
    ];

    private static readonly string[] FaultPacketTypes = [
        "ChaseBehaviorPacket",
        "DigInMudBehaviorPacket",
        "EatPickableBehaviorPacket",
        "FishOutOfWaterBehaviorPacket",
        "LookAroundBehaviorPacket",
        "RandomFeedBehaviorPacket",
        "RandomPeckBehaviorPacket",
        "StubbornSteedBehaviorPacket",
        "ClothingSyncPacket",
        "VitalStatSyncPacket",
        "ComponentFluPacket",
        "ComponentSicknessPacket",
        "PlayerAddExperiencePacket",
        "PlayerStatSyncPacket",
        "RequestSkinPacket",
        "ReplySkinPacket"
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
        new("reset-events", ControlCommandNames.ResetEvents),
        new("prepare-survival-fixture", ControlCommandNames.PrepareSurvivalStateFixture),
        new("wait-fixture-replication", ControlCommandNames.GetSnapshotV2),
        new("quiesce-fixture-nodes", ControlCommandNames.QuiesceSurvivalStateFixture),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("set-creature-behavior", ControlCommandNames.SetCreatureBehaviorState),
        new("set-player-survival-state", ControlCommandNames.SetPlayerSurvivalState),
        new("request-actor-skin", ControlCommandNames.RequestSkinTransfer),
        new("trigger-server-sound", ControlCommandNames.TriggerCreatureSound),
        new("verify-server-local-sound", ControlCommandNames.GetSnapshotV2),
        new("trigger-client-sound", ControlCommandNames.TriggerCreatureSound),
        new("verify-client-local-sound", ControlCommandNames.GetSnapshotV2),
        new("verify-initial-convergence", ControlCommandNames.GetSnapshotV2),
        new("forge-client-state", ControlCommandNames.ForgeSurvivalState),
        new("verify-server-authority", ControlCommandNames.GetSnapshotV2),
        new("run-revision-burst", ControlCommandNames.RunSurvivalStateRevisionBurst),
        new("verify-revision-convergence", ControlCommandNames.GetSnapshotV2),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("connect-late-client", "network.connect(client-c)"),
        new("quiesce-late-client", ControlCommandNames.QuiesceSurvivalStateFixture),
        new("verify-late-join", ControlCommandNames.GetSnapshotV2),
        new("request-late-join-skin", ControlCommandNames.RequestSkinTransfer),
        new("verify-late-join-skin", ControlCommandNames.GetSnapshotV2),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("reconnect-actor", "network.connect(client-a)"),
        new("quiesce-reconnected-actor", ControlCommandNames.QuiesceSurvivalStateFixture),
        new("verify-reconnect", ControlCommandNames.GetSnapshotV2),
        new("capture-before-save", ControlCommandNames.GetSnapshotV2),
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
            Seed: unchecked(seed + 947),
            BaseLatencyMilliseconds: 15,
            JitterMilliseconds: 3,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 20,
            TargetPacketTypes: FaultPacketTypes);

    public static string[] CreateFaultPacketTypes() => [.. FaultPacketTypes];

    public static SurvivalStateReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        (string Role, string Command)[] expected = [
            ("server", ControlCommandNames.SetCreatureBehaviorState),
            ("server", ControlCommandNames.SetPlayerSurvivalState),
            ("client-a", ControlCommandNames.RequestSkinTransfer),
            ("server", ControlCommandNames.TriggerCreatureSound),
            ("client-a", ControlCommandNames.TriggerCreatureSound),
            ("client-a", ControlCommandNames.ForgeSurvivalState),
            ("server", ControlCommandNames.RunSurvivalStateRevisionBurst),
            ("client-c", ControlCommandNames.RequestSkinTransfer)
        ];
        if (records.Count != expected.Length) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly {expected.Length} recorded actions.");
        }
        for (int index = 0; index < expected.Length; index++) {
            if (!string.Equals(records[index].Role, expected[index].Role, StringComparison.Ordinal)
                || !string.Equals(records[index].Command, expected[index].Command, StringComparison.Ordinal)) {
                throw new InvalidDataException(
                    $"Replay action {index} must be {expected[index].Role}:{expected[index].Command}, "
                    + $"got {records[index].Role}:{records[index].Command}.");
            }
        }
        return new SurvivalStateReplayPlan(
            DeserializeAction<SetCreatureBehaviorStateCommand>(records[0]),
            DeserializeAction<SetPlayerSurvivalStateCommand>(records[1]),
            DeserializeAction<RequestSkinTransferCommand>(records[2]),
            DeserializeAction<TriggerCreatureSoundCommand>(records[3]),
            DeserializeAction<TriggerCreatureSoundCommand>(records[4]),
            DeserializeAction<ForgeSurvivalStateCommand>(records[5]),
            DeserializeAction<RunSurvivalStateRevisionBurstCommand>(records[6]),
            DeserializeAction<RequestSkinTransferCommand>(records[7]));
    }

    public async Task<SurvivalStateMultiplayerScenarioResult> RunAsync(
        SurvivalStateMultiplayerScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory, options.Transport);
        NetworkFaultProfile faultProfile = (options.NetworkFaults
            ?? CreateDefaultNetworkFaults(options.Seed)).Validate();
        ValidateRequiredFaultProfile(faultProfile);
        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(75);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(14);
        if (stepTimeout <= TimeSpan.Zero || scenarioTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.StepTimeout));
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
        SurvivalStateFixture? fixture = null;
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
                    ControlCommandNames.PrepareSurvivalStateFixture,
                    ControlCommandNames.QuiesceSurvivalStateFixture,
                    ControlCommandNames.SetCreatureBehaviorState,
                    ControlCommandNames.TriggerCreatureSound,
                    ControlCommandNames.SetPlayerSurvivalState,
                    ControlCommandNames.RequestSkinTransfer,
                    ControlCommandNames.RunSurvivalStateRevisionBurst,
                    ControlCommandNames.ForgeSurvivalState,
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
                    AutomationCommandDomain.Entity,
                    AutomationCommandDomain.Inventory,
                    AutomationCommandDomain.Mechanics,
                    AutomationCommandDomain.State
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"SurvivalState-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken,
                creative: false).ConfigureAwait(false);
            initialActorPlayerIndex = actorPlayerIndex;
            GameNodeProcess actorClient = cluster.Clients[0];
            GameNodeProcess observerClient = cluster.Clients[1];
            GameNodeProcess lateClient = cluster.Clients[2];
            GameNodeProcess[] initialNodes = [cluster.Server, actorClient, observerClient];

            await runner.RunAsync(
                "reset-events",
                ControlCommandNames.ResetEvents,
                token => Task.WhenAll(initialNodes.Select(node =>
                    node.SendAsync<EmptyCommand, ResetEventsResult>(
                        ControlCommandNames.ResetEvents,
                        new EmptyCommand(),
                        cancellationToken: token))),
                scenarioToken).ConfigureAwait(false);

            fixture = await runner.RunAsync(
                "prepare-survival-fixture",
                ControlCommandNames.PrepareSurvivalStateFixture,
                token => cluster.Server.SendAsync<PrepareSurvivalStateFixtureCommand, SurvivalStateFixture>(
                    ControlCommandNames.PrepareSurvivalStateFixture,
                    new PrepareSurvivalStateFixtureCommand(
                        actorPlayerIndex,
                        $"AutotestNetworkSkin-{options.Seed:X8}.scskin"),
                    timeout: stepTimeout,
                    cancellationToken: token),
                value => $"player={value.PlayerIndex}; entities={string.Join(',', value.Behaviors.EntityIds)}",
                scenarioToken).ConfigureAwait(false);

            Dictionary<string, StateSnapshotV2> fixtureSnapshots = await runner.RunAsync(
                "wait-fixture-replication",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForStateOnNodesAsync(
                    initialNodes,
                    fixture,
                    expected: null,
                    requireSkinResource: _ => false,
                    requireIgnoredStale: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, fixtureSnapshots, "fixture");

            await runner.RunAsync(
                "quiesce-fixture-nodes",
                ControlCommandNames.QuiesceSurvivalStateFixture,
                token => QuiesceFixtureNodesAsync(initialNodes, fixture, token),
                scenarioToken).ConfigureAwait(false);

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    MultiplayerGameplayBootstrap.AllNodes(cluster).ToArray(),
                    faultProfile,
                    token),
                scenarioToken).ConfigureAwait(false);

            SetCreatureBehaviorStateCommand behaviorCommand = options.ReplayPlan?.BehaviorState
                ?? new SetCreatureBehaviorStateCommand(fixture.Behaviors);
            EnsureBehaviorFixture(behaviorCommand.Fixture, fixture.Behaviors, "behaviorState");
            CreatureBehaviorStateResult behaviorResult = await runner.RunAsync(
                "set-creature-behavior",
                ControlCommandNames.SetCreatureBehaviorState,
                token => cluster.Server.SendAsync<SetCreatureBehaviorStateCommand, CreatureBehaviorStateResult>(
                    ControlCommandNames.SetCreatureBehaviorState,
                    behaviorCommand,
                    cancellationToken: token),
                result => $"revision>={result.MinimumRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(behaviorResult.Receipt, ProductionPathEntries.CreatureBehaviorState);
            actions.Add(CreateAction(cluster.Server, ControlCommandNames.SetCreatureBehaviorState, behaviorCommand, behaviorResult.Receipt));
            events["server-behavior"] = await WaitForTerminalEventAsync(cluster.Server, behaviorResult.Receipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            SetPlayerSurvivalStateCommand playerCommand = options.ReplayPlan?.PlayerState
                ?? new SetPlayerSurvivalStateCommand(fixture);
            EnsureFixture(playerCommand.Fixture, fixture, "playerState");
            PlayerSurvivalStateResult playerResult = await runner.RunAsync(
                "set-player-survival-state",
                ControlCommandNames.SetPlayerSurvivalState,
                token => cluster.Server.SendAsync<SetPlayerSurvivalStateCommand, PlayerSurvivalStateResult>(
                    ControlCommandNames.SetPlayerSurvivalState,
                    playerCommand,
                    cancellationToken: token),
                result => $"level={result.Level:R}; blocksDug={result.BlocksDug}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(playerResult.Receipt, ProductionPathEntries.PlayerProgressionVitals);
            actions.Add(CreateAction(cluster.Server, ControlCommandNames.SetPlayerSurvivalState, playerCommand, playerResult.Receipt));
            events["server-player-state"] = await WaitForTerminalEventAsync(cluster.Server, playerResult.Receipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            RequestSkinTransferCommand actorSkinCommand = options.ReplayPlan?.ActorSkinRequest
                ?? new RequestSkinTransferCommand(actorPlayerIndex, fixture.SkinName);
            EnsureSkinCommand(actorSkinCommand, actorPlayerIndex, fixture.SkinName, "actorSkinRequest");
            ActionReceipt actorSkinReceipt = await runner.RunAsync(
                "request-actor-skin",
                ControlCommandNames.RequestSkinTransfer,
                token => actorClient.SendAsync<RequestSkinTransferCommand, ActionReceipt>(
                    ControlCommandNames.RequestSkinTransfer,
                    actorSkinCommand,
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureAccepted(actorSkinReceipt, ProductionPathEntries.SkinTransfer);
            actions.Add(CreateAction(actorClient, ControlCommandNames.RequestSkinTransfer, actorSkinCommand, actorSkinReceipt));
            events["client-a-skin"] = await WaitForTerminalEventAsync(actorClient, actorSkinReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            TriggerCreatureSoundCommand serverSoundCommand = options.ReplayPlan?.ServerSound
                ?? new TriggerCreatureSoundCommand(fixture.Behaviors.SoundEntityId, CreatureSoundKind.Idle);
            EnsureSoundCommand(serverSoundCommand, fixture, "serverSound");
            ActionReceipt serverSoundReceipt = await runner.RunAsync(
                "trigger-server-sound",
                ControlCommandNames.TriggerCreatureSound,
                token => cluster.Server.SendAsync<TriggerCreatureSoundCommand, ActionReceipt>(
                    ControlCommandNames.TriggerCreatureSound,
                    serverSoundCommand,
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureAccepted(serverSoundReceipt, ProductionPathEntries.CreatureSoundLocal);
            actions.Add(CreateAction(cluster.Server, ControlCommandNames.TriggerCreatureSound, serverSoundCommand, serverSoundReceipt));
            events["server-sound"] = await WaitForTerminalEventAsync(cluster.Server, serverSoundReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            Dictionary<string, StateSnapshotV2> serverSoundSnapshots = await runner.RunAsync(
                "verify-server-local-sound",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForSoundIsolationAsync(
                    initialNodes,
                    fixture,
                    playedRoles: ["server"],
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, serverSoundSnapshots, "server-sound");

            TriggerCreatureSoundCommand clientSoundCommand = options.ReplayPlan?.ClientSound
                ?? new TriggerCreatureSoundCommand(fixture.Behaviors.SoundEntityId, CreatureSoundKind.Cough);
            EnsureSoundCommand(clientSoundCommand, fixture, "clientSound");
            ActionReceipt clientSoundReceipt = await runner.RunAsync(
                "trigger-client-sound",
                ControlCommandNames.TriggerCreatureSound,
                token => actorClient.SendAsync<TriggerCreatureSoundCommand, ActionReceipt>(
                    ControlCommandNames.TriggerCreatureSound,
                    clientSoundCommand,
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureAccepted(clientSoundReceipt, ProductionPathEntries.CreatureSoundLocal);
            actions.Add(CreateAction(actorClient, ControlCommandNames.TriggerCreatureSound, clientSoundCommand, clientSoundReceipt));
            events["client-a-sound"] = await WaitForTerminalEventAsync(actorClient, clientSoundReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            Dictionary<string, StateSnapshotV2> clientSoundSnapshots = await runner.RunAsync(
                "verify-client-local-sound",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForSoundIsolationAsync(
                    initialNodes,
                    fixture,
                    playedRoles: ["server", "client-a"],
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, clientSoundSnapshots, "client-sound");
            assertions.Add(new ScenarioAssertionRecordV2(
                "creature-sound-is-locally-derived",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "Server and client sounds changed only the node that invoked ComponentCreatureSounds; no sound packet was replayed to the observer."));

            SurvivalExpectedState expected = new(
                playerResult.Level,
                playerResult.BlocksDug,
                fixture.ClothingValue,
                playerCommand.Food,
                RequireFlu: true,
                RequireSickness: true);
            Dictionary<string, StateSnapshotV2> initialConvergence = await runner.RunAsync(
                "verify-initial-convergence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForStateOnNodesAsync(
                    initialNodes,
                    fixture,
                    expected,
                    requireSkinResource: node => node.Role is "server" or "client-a",
                    requireIgnoredStale: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, initialConvergence, "initial-convergence");
            assertions.Add(new ScenarioAssertionRecordV2(
                "survival-state-initial-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"player={actorPlayerIndex}; behavior=enabled; clothing={fixture.ClothingValue}; level={playerResult.Level:R}; flu=true; sickness=true"));

            StateSnapshotV2 authorityBefore = initialConvergence["server"];
            ForgeSurvivalStateCommand forgeCommand = options.ReplayPlan?.AuthorityProbe
                ?? new ForgeSurvivalStateCommand(fixture);
            EnsureFixture(forgeCommand.Fixture, fixture, "authorityProbe");
            ActionReceipt forgeReceipt = await runner.RunAsync(
                "forge-client-state",
                ControlCommandNames.ForgeSurvivalState,
                token => actorClient.SendAsync<ForgeSurvivalStateCommand, ActionReceipt>(
                    ControlCommandNames.ForgeSurvivalState,
                    forgeCommand,
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureAccepted(forgeReceipt, ProductionPathEntries.SurvivalStateFault);
            actions.Add(CreateAction(actorClient, ControlCommandNames.ForgeSurvivalState, forgeCommand, forgeReceipt));
            events["client-a-authority"] = await WaitForTerminalEventAsync(actorClient, forgeReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 authorityAfter = await runner.RunAsync(
                "verify-server-authority",
                ControlCommandNames.GetSnapshotV2,
                async token => {
                    await Task.Delay(TimeSpan.FromMilliseconds(750), token).ConfigureAwait(false);
                    return await CaptureSurvivalSnapshotAsync(cluster.Server, fixture, token).ConfigureAwait(false);
                },
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureAuthoritativeStateUnchanged(authorityBefore, authorityAfter, fixture);
            snapshots["server-after-authority-probe"] = authorityAfter;
            assertions.Add(new ScenarioAssertionRecordV2(
                "survival-state-server-authority",
                "passed",
                ["server", "client-a"],
                "Client-forged behavior, clothing, progression, stats, vitals, flu and sickness packets did not mutate server state."));

            RunSurvivalStateRevisionBurstCommand burstCommand = options.ReplayPlan?.RevisionBurst
                ?? new RunSurvivalStateRevisionBurstCommand(fixture);
            EnsureFixture(burstCommand.Fixture, fixture, "revisionBurst");
            SurvivalStateRevisionBurstResult burst = await runner.RunAsync(
                "run-revision-burst",
                ControlCommandNames.RunSurvivalStateRevisionBurst,
                token => cluster.Server.SendAsync<RunSurvivalStateRevisionBurstCommand, SurvivalStateRevisionBurstResult>(
                    ControlCommandNames.RunSurvivalStateRevisionBurst,
                    burstCommand,
                    cancellationToken: token),
                value => $"revision>={value.MinimumFinalRevision}; stale={burstCommand.SendStaleStates}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(burst.Receipt, ProductionPathEntries.SurvivalStateFault);
            actions.Add(CreateAction(cluster.Server, ControlCommandNames.RunSurvivalStateRevisionBurst, burstCommand, burst.Receipt));
            events["server-revision-burst"] = await WaitForTerminalEventAsync(cluster.Server, burst.Receipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            expected = expected with {
                Level = burst.FinalLevel,
                BlocksDug = burst.FinalBlocksDug,
                Food = burst.FinalFood
            };
            Dictionary<string, StateSnapshotV2> revisionSnapshots = await runner.RunAsync(
                "verify-revision-convergence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForStateOnNodesAsync(
                    initialNodes,
                    fixture,
                    expected,
                    requireSkinResource: node => node.Role is "server" or "client-a",
                    requireIgnoredStale: true,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, revisionSnapshots, "revision-convergence");
            long duplicates = revisionSnapshots.Values.Sum(snapshot => snapshot.Network?.FaultDuplicated ?? 0);
            if (duplicates <= 0) {
                throw new InvalidOperationException("Targeted survival-state fault profile did not duplicate any packets.");
            }
            metrics["fault.duplicates"] = duplicates.ToString(CultureInfo.InvariantCulture);
            assertions.Add(new ScenarioAssertionRecordV2(
                "survival-state-duplicate-reorder-stale-resilience",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"ignoredStale>0 on clients; duplicatedPackets={duplicates}; finalRevision>={burst.MinimumFinalRevision}"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    MultiplayerGameplayBootstrap.AllNodes(cluster).ToArray(),
                    new NetworkFaultProfile(Seed: options.Seed),
                    token),
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
                        "Server did not expose the late-join session.",
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "quiesce-late-client",
                ControlCommandNames.QuiesceSurvivalStateFixture,
                token => QuiesceFixtureNodesAsync([lateClient], fixture, token),
                scenarioToken).ConfigureAwait(false);
            GameNodeProcess[] allNodes = [cluster.Server, actorClient, observerClient, lateClient];
            GameNodeProcess[] lateJoinNodes = [cluster.Server, lateClient];
            Dictionary<string, StateSnapshotV2> lateSnapshots = await runner.RunAsync(
                "verify-late-join",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForStateOnNodesAsync(
                    lateJoinNodes,
                    fixture,
                    expected,
                    requireSkinResource: node => node.Role == "server",
                    requireIgnoredStale: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureSoundNotPlayed(lateSnapshots["client-c"], fixture.Behaviors.SoundEntityId, "late join");
            AddSnapshots(snapshots, lateSnapshots, "late-join");

            NodeStatus lateStatus = await lateClient.GetStatusAsync(scenarioToken).ConfigureAwait(false);
            RequestSkinTransferCommand lateSkinCommand = options.ReplayPlan?.LateJoinSkinRequest
                ?? new RequestSkinTransferCommand(lateStatus.PlayerIndex, fixture.SkinName);
            EnsureSkinCommand(lateSkinCommand, lateStatus.PlayerIndex, fixture.SkinName, "lateJoinSkinRequest");
            ActionReceipt lateSkinReceipt = await runner.RunAsync(
                "request-late-join-skin",
                ControlCommandNames.RequestSkinTransfer,
                token => lateClient.SendAsync<RequestSkinTransferCommand, ActionReceipt>(
                    ControlCommandNames.RequestSkinTransfer,
                    lateSkinCommand,
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureAccepted(lateSkinReceipt, ProductionPathEntries.SkinTransfer);
            actions.Add(CreateAction(lateClient, ControlCommandNames.RequestSkinTransfer, lateSkinCommand, lateSkinReceipt));
            events["client-c-skin"] = await WaitForTerminalEventAsync(lateClient, lateSkinReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 lateSkinSnapshot = await runner.RunAsync(
                "verify-late-join-skin",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForStateAsync(
                    lateClient,
                    fixture,
                    expected,
                    requireSkinResource: true,
                    requireIgnoredStale: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            snapshots["client-c-after-skin-transfer"] = lateSkinSnapshot;
            assertions.Add(new ScenarioAssertionRecordV2(
                "survival-state-late-join-baseline",
                "passed",
                lateJoinNodes.Select(node => node.Role).ToArray(),
                "Late client received current behavior/clothing/status/progression/vitals state and fetched the bounded custom skin; historical sounds were not replayed."));

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
                        "Actor client did not disconnect.",
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
                status => $"player={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            EnsureActorIdentity(reconnectStatus, initialActorPlayerIndex, "reconnect");
            await runner.RunAsync(
                "quiesce-reconnected-actor",
                ControlCommandNames.QuiesceSurvivalStateFixture,
                token => QuiesceFixtureNodesAsync([actorClient], fixture, token),
                scenarioToken).ConfigureAwait(false);
            GameNodeProcess[] reconnectNodes = [cluster.Server, actorClient, lateClient];
            Dictionary<string, StateSnapshotV2> reconnectSnapshots = await runner.RunAsync(
                "verify-reconnect",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForStateOnNodesAsync(
                    reconnectNodes,
                    fixture,
                    expected,
                    requireSkinResource: node => node.Role is "server" or "client-a" or "client-c",
                    requireIgnoredStale: false,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, reconnectSnapshots, "reconnect");
            assertions.Add(new ScenarioAssertionRecordV2(
                "survival-state-reconnect-baseline",
                "passed",
                reconnectNodes.Select(node => node.Role).ToArray(),
                $"playerIndex={initialActorPlayerIndex}; all authoritative state restored after reconnect"));

            StateSnapshotV2 beforeSave = await runner.RunAsync(
                "capture-before-save",
                ControlCommandNames.GetSnapshotV2,
                token => CaptureSurvivalSnapshotAsync(cluster.Server, fixture, token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            snapshots["server-before-save"] = beforeSave;
            SurvivalPlayerStateSnapshotV2 savedPlayer = RequirePlayer(beforeSave, fixture.PlayerIndex);
            string savedWorldDirectory = await SaveStopAndReloadAsync(
                cluster,
                runner,
                stepTimeout,
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
                    return await actorClient.GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"player={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            EnsureActorIdentity(persistedActor, initialActorPlayerIndex, "save/reload");
            GameNodeProcess[] persistenceNodes = [cluster.Server, actorClient];
            Dictionary<string, StateSnapshotV2> serverPersistence = await runner.RunAsync(
                "verify-server-persistence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForPersistedStateOnNodesAsync(
                    persistenceNodes,
                    fixture,
                    savedPlayer,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, serverPersistence, "server-persistence");

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
                },
                scenarioToken).ConfigureAwait(false);
            GameNodeProcess[] persistedObservationNodes = [cluster.Server, actorClient, lateClient];
            Dictionary<string, StateSnapshotV2> clientPersistence = await runner.RunAsync(
                "verify-client-persistence",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForPersistedStateOnNodesAsync(
                    persistedObservationNodes,
                    fixture,
                    savedPlayer,
                    stepTimeout,
                    token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, clientPersistence, "client-persistence");
            assertions.Add(new ScenarioAssertionRecordV2(
                "survival-state-save-reload-persistence",
                "passed",
                persistedObservationNodes.Select(node => node.Role).ToArray(),
                $"world={savedWorldDirectory}; clothing, skin, level, stats, vitals, flu and sickness survived save/reload"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFinalEvidenceAsync(cluster, fixture, snapshots, events).ConfigureAwait(false);
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
            throw new SurvivalStateMultiplayerScenarioException(
                $"Survival-state multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new SurvivalStateMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new SurvivalStateMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private sealed record SurvivalExpectedState(
        double Level,
        long BlocksDug,
        int ClothingValue,
        float Food,
        bool RequireFlu,
        bool RequireSickness);

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForStateOnNodesAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        SurvivalStateFixture fixture,
        SurvivalExpectedState? expected,
        Func<GameNodeProcess, bool>? requireSkinResource,
        bool requireIgnoredStale,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        GameNodeProcess[] ordered = nodes.ToArray();
        StateSnapshotV2[] captured = await Task.WhenAll(ordered.Select(node =>
            WaitForStateAsync(
                node,
                fixture,
                expected,
                requireSkinResource?.Invoke(node) == true,
                requireIgnoredStale && node.Role != "server",
                timeout,
                cancellationToken))).ConfigureAwait(false);
        return ordered.Select((node, index) => (node.Role, Snapshot: captured[index]))
            .ToDictionary(item => item.Role, item => item.Snapshot, StringComparer.Ordinal);
    }

    private static async Task<StateSnapshotV2> WaitForStateAsync(
        GameNodeProcess node,
        SurvivalStateFixture fixture,
        SurvivalExpectedState? expected,
        bool requireSkinResource,
        bool requireIgnoredStale,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureSurvivalSnapshotAsync(node, fixture, cancellationToken).ConfigureAwait(false);
            SurvivalPlayerStateSnapshotV2? player = last.SurvivalState?.Players.SingleOrDefault(item =>
                item.PlayerIndex == fixture.PlayerIndex);
            // A reconnect preserves PlayerData identity but legitimately creates a new
            // runtime player entity. PlayerIndex and authoritative state are the stable
            // identity contract across that lifecycle boundary.
            bool fixtureReady = player?.EntityId > 0
                && fixture.Behaviors.EntityIds.All(entityId =>
                    last.SurvivalState?.Creatures.Any(creature =>
                        creature.EntityId == entityId && creature.TemplateName != null) == true);
            bool stateReady = expected == null || player != null
                && Math.Abs(player.Level - expected.Level) <= 0.02
                && player.BlocksDug == expected.BlocksDug
                && HasClothing(player, fixture.ClothingSlotIndex, expected.ClothingValue)
                && string.Equals(player.SkinName, fixture.SkinName, StringComparison.Ordinal)
                && Math.Abs(player.Food - expected.Food) <= 0.2
                && (!expected.RequireFlu || player.HasFlu)
                && (!expected.RequireSickness || player.IsSick)
                && BehaviorsEnabled(last, fixture);
            bool skinReady = !requireSkinResource || player?.HasSkinResource == true;
            bool staleReady = !requireIgnoredStale
                || last.StateRevisions?.IgnoredStaleStates > 0;
            bool faultReady = last.Network?.FaultPending == 0;
            if (fixtureReady && stateReady && skinReady && staleReady && faultReady) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge survival state. Last="
            + JsonSerializer.Serialize(last?.SurvivalState, ControlProtocol.JsonOptions));
    }

    private static bool BehaviorsEnabled(StateSnapshotV2 snapshot, SurvivalStateFixture fixture) {
        CreatureBehaviorStateSnapshotV2[] creatures = snapshot.SurvivalState?.Creatures ?? [];
        CreatureBehaviorStateSnapshotV2? Find(int id) => creatures.SingleOrDefault(item => item.EntityId == id);
        return Find(fixture.Behaviors.ChaseEntityId)?.ChaseAttack == true
            && Find(fixture.Behaviors.DigInMudEntityId)?.DigInMud == true
            && Find(fixture.Behaviors.EatPickableEntityId)?.EatPickable == true
            && Find(fixture.Behaviors.FishOutOfWaterEntityId)?.FishOutOfWater == true
            && Find(fixture.Behaviors.LookAroundEntityId)?.LookAround == true
            && Find(fixture.Behaviors.RandomFeedEntityId)?.RandomFeed == true
            && Find(fixture.Behaviors.RandomPeckEntityId)?.RandomPeck == true
            && Find(fixture.Behaviors.StubbornSteedEntityId)?.HeadShakeOrder > 0.5;
    }

    private static bool HasClothing(
        SurvivalPlayerStateSnapshotV2 player,
        int slotIndex,
        int value) =>
        player.ClothingSlots.SingleOrDefault(slot => slot.SlotIndex == slotIndex)?.Values
            .Contains(value) == true;

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForSoundIsolationAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        SurvivalStateFixture fixture,
        IReadOnlyCollection<string> playedRoles,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, StateSnapshotV2> result = new(StringComparer.Ordinal);
            foreach (GameNodeProcess node in nodes) {
                result[node.Role] = await CaptureSurvivalSnapshotAsync(node, fixture, cancellationToken).ConfigureAwait(false);
            }
            bool matches = result.All(item => {
                CreatureBehaviorStateSnapshotV2 sound = RequireCreature(
                    item.Value,
                    fixture.Behaviors.SoundEntityId);
                bool played = sound.LastSoundTime > -999
                    || sound.LastCoughSoundTime > -999
                    || sound.LastPukeSoundTime > -999;
                return played == playedRoles.Contains(item.Key);
            });
            if (matches) return result;
            await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Creature sound timestamps were observed on an unexpected node.");
    }

    private static async Task QuiesceFixtureNodesAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        SurvivalStateFixture fixture,
        CancellationToken cancellationToken) {
        ControlAck[] acknowledgements = await Task.WhenAll(nodes.Select(node =>
            node.SendAsync<QuiesceSurvivalStateFixtureCommand, ControlAck>(
                ControlCommandNames.QuiesceSurvivalStateFixture,
                new QuiesceSurvivalStateFixtureCommand(fixture.Behaviors),
                cancellationToken: cancellationToken))).ConfigureAwait(false);
        if (acknowledgements.Any(acknowledgement => !acknowledgement.Accepted)) {
            throw new InvalidOperationException("A survival-state fixture node rejected quiescing.");
        }
    }

    private static void EnsureSoundNotPlayed(
        StateSnapshotV2 snapshot,
        int entityId,
        string phase) {
        CreatureBehaviorStateSnapshotV2 sound = RequireCreature(snapshot, entityId);
        if (sound.LastSoundTime > -999
            || sound.LastCoughSoundTime > -999
            || sound.LastPukeSoundTime > -999) {
            throw new InvalidOperationException($"Historical creature sound was replayed during {phase}.");
        }
    }

    private static async Task<StateSnapshotV2> CaptureSurvivalSnapshotAsync(
        GameNodeProcess node,
        SurvivalStateFixture fixture,
        CancellationToken cancellationToken) =>
        await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.SurvivalState],
                PlayerIndexes: [fixture.PlayerIndex],
                EntityIds: fixture.Behaviors.EntityIds),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    private static void EnsureAuthoritativeStateUnchanged(
        StateSnapshotV2 before,
        StateSnapshotV2 after,
        SurvivalStateFixture fixture) {
        SurvivalPlayerStateSnapshotV2 a = RequirePlayer(before, fixture.PlayerIndex);
        SurvivalPlayerStateSnapshotV2 b = RequirePlayer(after, fixture.PlayerIndex);
        if (Math.Abs(a.Level - b.Level) > 0.02
            || a.BlocksDug != b.BlocksDug
            || !HasClothing(b, fixture.ClothingSlotIndex, fixture.ClothingValue)
            || a.SkinName != b.SkinName
            || a.HasFlu != b.HasFlu
            || a.IsSick != b.IsSick
            || !BehaviorsEnabled(after, fixture)) {
            throw new InvalidOperationException("Client-forged authoritative state changed the server.");
        }
    }

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForPersistedStateOnNodesAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        SurvivalStateFixture fixture,
        SurvivalPlayerStateSnapshotV2 saved,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, StateSnapshotV2> result = new(StringComparer.Ordinal);
            bool ready = true;
            foreach (GameNodeProcess node in nodes) {
                StateSnapshotV2 snapshot = await CaptureSurvivalSnapshotAsync(node, fixture, cancellationToken).ConfigureAwait(false);
                result[node.Role] = snapshot;
                SurvivalPlayerStateSnapshotV2? player = snapshot.SurvivalState?.Players.SingleOrDefault(item =>
                    item.PlayerIndex == fixture.PlayerIndex);
                ready &= player != null
                    && Math.Abs(player.Level - saved.Level) <= 0.02
                    && player.BlocksDug == saved.BlocksDug
                    && HasClothing(player, fixture.ClothingSlotIndex, fixture.ClothingValue)
                    && string.Equals(player.SkinName, fixture.SkinName, StringComparison.Ordinal)
                    && player.HasFlu
                    && player.IsSick
                    && Math.Abs(player.Food - saved.Food) <= 0.3;
            }
            if (ready) return result;
            await Task.Delay(TimeSpan.FromMilliseconds(75), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Persisted survival state did not converge after save/reload.");
    }

    private static SurvivalPlayerStateSnapshotV2 RequirePlayer(
        StateSnapshotV2 snapshot,
        int playerIndex) =>
        snapshot.SurvivalState?.Players.SingleOrDefault(player => player.PlayerIndex == playerIndex)
        ?? throw new InvalidOperationException($"Snapshot lacks survival player {playerIndex}.");

    private static CreatureBehaviorStateSnapshotV2 RequireCreature(
        StateSnapshotV2 snapshot,
        int entityId) =>
        snapshot.SurvivalState?.Creatures.SingleOrDefault(creature => creature.EntityId == entityId)
        ?? throw new InvalidOperationException($"Snapshot lacks creature {entityId}.");

    private static async Task<ActionEventsResult> WaitForTerminalEventAsync(
        GameNodeProcess node,
        string actionId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            ActionEventsResult result = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                ControlCommandNames.GetEvents,
                new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            ActionEventRecord[] matching = result.Events.Where(item =>
                string.Equals(item.ActionId, actionId, StringComparison.Ordinal)).ToArray();
            if (matching.Any(item => item.Phase == ActionEventPhase.Rejected)) {
                throw new InvalidOperationException($"Runtime rejected action '{actionId}'.");
            }
            if (matching.Any(item => item.Phase is ActionEventPhase.Applied or ActionEventPhase.Observed)) {
                return result;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException($"Action '{actionId}' did not publish terminal evidence.");
    }

    private static async Task ConfigureNetworkFaultsAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        NetworkFaultProfile profile,
        CancellationToken cancellationToken) {
        GameNodeProcess[] ordered = nodes.ToArray();
        NetworkFaultState[] states = await Task.WhenAll(ordered.Select((node, index) =>
            node.SendAsync<SetNetworkFaultsCommand, NetworkFaultState>(
                ControlCommandNames.SetNetworkFaults,
                new SetNetworkFaultsCommand(profile with { Seed = unchecked(profile.Seed + index) }),
                cancellationToken: cancellationToken))).ConfigureAwait(false);
        if (states.Length != ordered.Length) {
            throw new InvalidOperationException("Not all nodes accepted the fault profile.");
        }
    }

    private static async Task<string> SaveStopAndReloadAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        string directory = await runner.RunAsync(
            "save-and-stop-world",
            ControlCommandNames.SaveWorld,
            async token => {
                SaveWorldResult saved = await cluster.Server.SendAsync<SaveWorldCommand, SaveWorldResult>(
                    ControlCommandNames.SaveWorld,
                    new SaveWorldCommand(),
                    timeout: timeout,
                    cancellationToken: token).ConfigureAwait(false);
                if (!saved.Saved || string.IsNullOrWhiteSpace(saved.DirectoryName)) {
                    throw new InvalidOperationException("Server declined to save the survival-state world.");
                }
                foreach (GameNodeProcess client in cluster.Clients) {
                    _ = await client.SendAsync<DisconnectCommand, ControlAck>(
                        ControlCommandNames.Disconnect,
                        new DisconnectCommand(),
                        cancellationToken: token).ConfigureAwait(false);
                }
                await Task.WhenAll(cluster.Clients.Select(client =>
                    client.WaitForStatusAsync(
                        status => !status.ProjectLoaded && !status.IsClientRunning,
                        timeout,
                        $"Node '{client.Role}' did not disconnect before reload.",
                        token))).ConfigureAwait(false);
                _ = await cluster.Server.WaitForStatusAsync(
                    status => status.ServerSessionCount == 0,
                    timeout,
                    "Server retained client sessions before reload.",
                    token).ConfigureAwait(false);
                _ = await cluster.Server.SendAsync<DisconnectCommand, ControlAck>(
                    ControlCommandNames.Disconnect,
                    new DisconnectCommand(SaveWorld: true),
                    timeout: timeout,
                    cancellationToken: token).ConfigureAwait(false);
                _ = await cluster.Server.WaitForStatusAsync(
                    status => !status.ProjectLoaded && !status.IsServerRunning,
                    timeout,
                    "Server did not stop after saving.",
                    token).ConfigureAwait(false);
                return saved.DirectoryName;
            },
            value => value,
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "reload-saved-world",
            ControlCommandNames.ReloadWorld,
            async token => {
                _ = await cluster.Server.SendAsync<ReloadWorldCommand, ControlAck>(
                    ControlCommandNames.ReloadWorld,
                    new ReloadWorldCommand(directory),
                    cancellationToken: token).ConfigureAwait(false);
                NodeStatus entry = await MultiplayerScenarioOperations.WaitForWorldEntryAsync(
                    cluster.Server,
                    serverSide: true,
                    timeout,
                    token).ConfigureAwait(false);
                if (entry.Screen == "PlayerScreen") {
                    await MultiplayerScenarioOperations.EnterWorldWhenReadyAsync(
                        cluster.Server,
                        "Host",
                        timeout,
                        token).ConfigureAwait(false);
                }
                _ = await cluster.Server.WaitForStatusAsync(
                    status => status.ProjectLoaded && status.IsServerRunning && status.MainPlayerReady,
                    timeout,
                    "Reloaded survival-state world did not become playable.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return directory;
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

    private static void EnsureAccepted(ActionReceipt receipt, string productionEntry) {
        if (!receipt.Accepted
            || string.IsNullOrWhiteSpace(receipt.ActionId)
            || !string.Equals(receipt.ProductionEntry, productionEntry, StringComparison.Ordinal)
            || receipt.Provenance is ActionProvenance.Unknown or ActionProvenance.Fixture) {
            throw new InvalidOperationException(
                $"Action '{receipt.ActionId}' did not attest production entry '{productionEntry}'.");
        }
    }

    private static void EnsureFixture(
        SurvivalStateFixture actual,
        SurvivalStateFixture expected,
        string name) {
        if (!Equals(actual, expected)) {
            throw new InvalidDataException($"Replay action '{name}' references a different fixture.");
        }
    }

    private static void EnsureBehaviorFixture(
        CreatureBehaviorFixture actual,
        CreatureBehaviorFixture expected,
        string name) {
        if (!Equals(actual, expected)) {
            throw new InvalidDataException($"Replay action '{name}' references a different behavior fixture.");
        }
    }

    private static void EnsureSoundCommand(
        TriggerCreatureSoundCommand command,
        SurvivalStateFixture fixture,
        string name) {
        if (command.EntityId != fixture.Behaviors.SoundEntityId || !Enum.IsDefined(command.SoundKind)) {
            throw new InvalidDataException($"Replay action '{name}' has an invalid sound target or kind.");
        }
    }

    private static void EnsureSkinCommand(
        RequestSkinTransferCommand command,
        int expectedPlayerIndex,
        string expectedSkinName,
        string name) {
        if (command.PlayerIndex != expectedPlayerIndex
            || !string.Equals(command.SkinName, expectedSkinName, StringComparison.Ordinal)) {
            throw new InvalidDataException($"Replay action '{name}' has an invalid player or skin name.");
        }
    }

    private static void EnsureActorIdentity(NodeStatus status, int expectedPlayerIndex, string phase) {
        if (!status.MainPlayerReady || status.PlayerIndex != expectedPlayerIndex) {
            throw new InvalidOperationException(
                $"Actor identity changed during {phase}. Expected={expectedPlayerIndex}; actual={status.PlayerIndex}.");
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

    private static async Task CaptureFinalEvidenceAsync(
        MultiplayerCluster cluster,
        SurvivalStateFixture? fixture,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) continue;
            try {
                GetSnapshotV2Command command = fixture == null
                    ? new GetSnapshotV2Command([
                        SnapshotDomain.Network,
                        SnapshotDomain.Players,
                        SnapshotDomain.Events,
                        SnapshotDomain.SurvivalState
                    ], EventLimit: AutomationProtocol.MaximumEventQueryCount)
                    : new GetSnapshotV2Command([
                        SnapshotDomain.Network,
                        SnapshotDomain.Players,
                        SnapshotDomain.Events,
                        SnapshotDomain.SurvivalState
                    ],
                        PlayerIndexes: [fixture.PlayerIndex],
                        EntityIds: fixture.Behaviors.EntityIds,
                        EventLimit: AutomationProtocol.MaximumEventQueryCount);
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
                // Best-effort evidence collection must not hide the original failure.
            }
            try {
                events[node.Role] = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                    ControlCommandNames.GetEvents,
                    new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Process logs remain available if a node is no longer responsive.
            }
        }
    }

    private static void ValidateRequiredFaultProfile(NetworkFaultProfile profile) {
        string[] targets = profile.TargetPacketTypes ?? [];
        if (profile.DuplicationRate <= 0
            || profile.ReorderRate <= 0
            || !targets.SequenceEqual(FaultPacketTypes, StringComparer.Ordinal)) {
            throw new ArgumentException(
                "Survival-state resilience requires duplicate/reorder faults targeted to every covered packet family.");
        }
    }

    private static void ValidateRuntime(
        string runtimeDirectory,
        MultiplayerTransportKind transport) {
        if (!Directory.Exists(runtimeDirectory)) {
            throw new DirectoryNotFoundException($"Game runtime directory '{runtimeDirectory}' does not exist.");
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
        string[] missing = required.Where(path => !File.Exists(Path.Combine(runtimeDirectory, path))).ToArray();
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
        return candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
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
            // Retain the isolated run directory if cleanup cannot be proven safe.
        }
    }
}
