using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record TerrainStreamingReplayPlan(
    PlayerMoveCommand Move,
    RunTerrainRevisionBurstCommand RevisionBurst);

public sealed record TerrainStreamingMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    TerrainStreamingReplayPlan? ReplayPlan = null);

public sealed record TerrainStreamingMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class TerrainStreamingMultiplayerScenarioException : Exception {
    public TerrainStreamingMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class TerrainStreamingMultiplayerScenario {
    public const string ScenarioName = "terrain-streaming-multiplayer";
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
        new("require-v2-capabilities", ControlCommandNames.Describe),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("start-transport-host", "network.startHost"),
        new("connect-initial-clients", "network.connect(client-a, client-b)"),
        new("resolve-actor", ControlCommandNames.GetStatus),
        new("verify-authority", "typed authority rejection"),
        new("prepare-streaming-region", ControlCommandNames.PrepareTerrainStreamingFixture),
        new("apply-initial-views", ControlCommandNames.ApplyTerrainStreamingView),
        new("verify-streaming-boundary", ControlCommandNames.GetSnapshotV2),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("execute-boundary-move", ControlCommandNames.PlayerMove),
        new("verify-chunk-streamed", ControlCommandNames.GetSnapshotV2),
        new("execute-revision-burst", ControlCommandNames.RunTerrainRevisionBurst),
        new("verify-revision-convergence", ControlCommandNames.GetSnapshotV2),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("connect-late-client", "network.connect(client-c)"),
        new("verify-late-join", ControlCommandNames.GetSnapshotV2),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("reconnect-actor", "network.connect(client-a)"),
        new("verify-reconnect", ControlCommandNames.GetSnapshotV2),
        new("save-and-stop-world", ControlCommandNames.SaveWorld),
        new("reload-saved-world", ControlCommandNames.ReloadWorld),
        new("restart-transport-host", "network.startHost"),
        new("connect-persistence-observer", "network.connect(client-c)"),
        new("verify-persistence-boundary", ControlCommandNames.GetSnapshotV2)
    ];

    public static NetworkFaultProfile CreateDefaultNetworkFaults(int seed) =>
        new(
            Seed: unchecked(seed + 509),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18);

    public static TerrainStreamingReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != 2) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly two recorded actions.");
        }
        if (!string.Equals(records[0].Role, "client-a", StringComparison.Ordinal)
            || !string.Equals(records[0].Command, ControlCommandNames.PlayerMove, StringComparison.Ordinal)) {
            throw new InvalidDataException(
                "Replay action 0 must be client-a/player.move.");
        }
        if (!string.Equals(records[1].Role, "server", StringComparison.Ordinal)
            || !string.Equals(
                records[1].Command,
                ControlCommandNames.RunTerrainRevisionBurst,
                StringComparison.Ordinal)) {
            throw new InvalidDataException(
                "Replay action 1 must be server/terrain.runRevisionBurst.");
        }
        TerrainStreamingReplayPlan plan = new(
            DeserializeAction<PlayerMoveCommand>(records[0]),
            DeserializeAction<RunTerrainRevisionBurstCommand>(records[1]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<TerrainStreamingMultiplayerScenarioResult> RunAsync(
        TerrainStreamingMultiplayerScenarioOptions options,
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
                "Terrain revision resilience requires a non-empty network fault profile.",
                nameof(options.NetworkFaults));
        }

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(8);
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
        TerrainStreamingFixture? fixture = null;
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
                    ControlCommandNames.PrepareTerrainStreamingFixture,
                    ControlCommandNames.ApplyTerrainStreamingView,
                    ControlCommandNames.PlayerMove,
                    ControlCommandNames.RunTerrainRevisionBurst
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Player,
                    AutomationCommandDomain.Terrain
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"TerrainStreaming-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            int initialActorPlayerIndex = actorPlayerIndex;
            NodeStatus observerStatus = await cluster.Clients[1].GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);
            if (observerStatus.PlayerIndex < 0 || !observerStatus.MainPlayerReady) {
                throw new InvalidOperationException(
                    $"Client B did not expose a ready observer. Status={observerStatus}.");
            }
            int observerPlayerIndex = observerStatus.PlayerIndex;
            metrics["actor.initialPlayerIndex"] = actorPlayerIndex.ToString(CultureInfo.InvariantCulture);
            metrics["observer.playerIndex"] = observerPlayerIndex.ToString(CultureInfo.InvariantCulture);

            _ = await WaitForPlayerReadyAsync(
                cluster.Clients[1],
                actorPlayerIndex,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            await VerifyAuthorityRejectionsAsync(
                cluster,
                runner,
                actorPlayerIndex,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "terrain-streaming-authority-enforcement",
                "passed",
                [cluster.Server.Role, cluster.Clients[0].Role, cluster.Clients[1].Role],
                "server cannot issue player.move; clients cannot issue revision bursts; non-owner cannot apply the actor view"));
            await runner.RunAsync(
                "reset-events",
                ControlCommandNames.ResetEvents,
                token => Task.WhenAll(MultiplayerGameplayBootstrap.AllNodes(cluster).Select(node =>
                    node.SendAsync<EmptyCommand, ResetEventsResult>(
                        ControlCommandNames.ResetEvents,
                        new EmptyCommand(),
                        cancellationToken: token))),
                scenarioToken).ConfigureAwait(false);

            fixture = await runner.RunAsync(
                "prepare-streaming-region",
                ControlCommandNames.PrepareTerrainStreamingFixture,
                token => cluster.Server.SendAsync<PrepareTerrainStreamingFixtureCommand, TerrainStreamingFixture>(
                    ControlCommandNames.PrepareTerrainStreamingFixture,
                    new PrepareTerrainStreamingFixtureCommand(actorPlayerIndex, observerPlayerIndex),
                    timeout: stepTimeout,
                    cancellationToken: token),
                result => $"probe={FormatCell(result.ProbeCell)}",
                scenarioToken).ConfigureAwait(false);
            EnsureFixture(fixture, actorPlayerIndex, observerPlayerIndex);

            await runner.RunAsync(
                "apply-initial-views",
                ControlCommandNames.ApplyTerrainStreamingView,
                token => Task.WhenAll(
                    ApplyViewAsync(cluster.Clients[0], fixture, actor: true, freezeBody: true, armMove: false, token),
                    ApplyViewAsync(cluster.Clients[1], fixture, actor: false, freezeBody: true, armMove: false, token)),
                scenarioToken).ConfigureAwait(false);

            TerrainCellCoordinate standingCell = ActorStandingCell(fixture);
            snapshots["client-a-standing-ready"] = await WaitForCellAsync(
                cluster.Clients[0],
                standingCell,
                expectedValue: null,
                expectedRevision: null,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["client-a-before-boundary"] = await WaitForCellAsync(
                cluster.Clients[0],
                fixture.ProbeCell,
                expectedValue: null,
                expectedRevision: null,
                requireLoaded: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 serverBaseline = await WaitForCellAsync(
                cluster.Server,
                fixture.ProbeCell,
                fixture.InitialValue,
                expectedRevision: null,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            long baselineRevision = FindCell(serverBaseline, fixture.ProbeCell).Revision;
            StateSnapshotV2 observerBaseline = await WaitForCellAsync(
                cluster.Clients[1],
                fixture.ProbeCell,
                fixture.InitialValue,
                baselineRevision,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-streaming-baseline"] = serverBaseline;
            snapshots["client-b-streaming-baseline"] = observerBaseline;
            assertions.Add(new ScenarioAssertionRecordV2(
                "terrain-loadedness-boundary",
                "passed",
                [cluster.Server.Role, cluster.Clients[0].Role, cluster.Clients[1].Role],
                $"actorProbeLoaded=false; observerProbeLoaded=true; revision={baselineRevision}"));

            await runner.RunAsync(
                "arm-boundary-move",
                ControlCommandNames.ApplyTerrainStreamingView,
                token => ApplyViewAsync(
                    cluster.Clients[0],
                    fixture,
                    actor: true,
                    freezeBody: false,
                    armMove: true,
                    token),
                scenarioToken).ConfigureAwait(false);

            GameNodeProcess[] initialNodes = [cluster.Server, cluster.Clients[0], cluster.Clients[1]];
            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            PlayerMoveCommand move = (options.ReplayPlan?.Move
                ?? new PlayerMoveCommand(
                    actorPlayerIndex,
                    fixture.MoveInput,
                    fixture.MoveHoldFrames)) with {
                PlayerIndex = actorPlayerIndex
            };
            ActionReceipt moveReceipt = await runner.RunAsync(
                "execute-boundary-move",
                ControlCommandNames.PlayerMove,
                token => cluster.Clients[0].SendAsync<PlayerMoveCommand, ActionReceipt>(
                    ControlCommandNames.PlayerMove,
                    move,
                    cancellationToken: token),
                result => $"actionId={result.ActionId}; code={result.Code}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(moveReceipt);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.PlayerMove, move, moveReceipt));
            events["client-a-move"] = await WaitForAppliedEventAsync(
                cluster.Clients[0],
                moveReceipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            StateSnapshotV2 actorStreamed = await WaitForCellAsync(
                cluster.Clients[0],
                fixture.ProbeCell,
                fixture.InitialValue,
                baselineRevision,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 serverAfterMove = await WaitForServerStreamingLocationAsync(
                cluster.Server,
                fixture,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["client-a-after-boundary-move"] = actorStreamed;
            snapshots["server-after-boundary-move"] = serverAfterMove;
            assertions.Add(new ScenarioAssertionRecordV2(
                "terrain-production-streaming-after-player-move",
                "passed",
                [cluster.Clients[0].Role, cluster.Server.Role],
                $"probe={FormatCell(fixture.ProbeCell)}; revision={baselineRevision}"));

            RunTerrainRevisionBurstCommand burst = (options.ReplayPlan?.RevisionBurst
                ?? new RunTerrainRevisionBurstCommand(
                    fixture.ProbeCell,
                    fixture.RevisionValues,
                    SendStaleSnapshot: true)) with {
                Cell = fixture.ProbeCell
            };
            TerrainRevisionBurstResult burstResult = await runner.RunAsync(
                "execute-revision-burst",
                ControlCommandNames.RunTerrainRevisionBurst,
                token => cluster.Server.SendAsync<RunTerrainRevisionBurstCommand, TerrainRevisionBurstResult>(
                    ControlCommandNames.RunTerrainRevisionBurst,
                    burst,
                    cancellationToken: token),
                result => $"baseline={result.BaselineRevision}; final={result.FinalRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(burstResult.Receipt);
            if (burstResult.BaselineRevision != baselineRevision
                || burstResult.FinalRevision != baselineRevision + burst.Values.Length
                || burstResult.FinalValue != burst.Values[^1]) {
                throw new InvalidOperationException(
                    $"Unexpected revision burst result: {burstResult}.");
            }
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.RunTerrainRevisionBurst,
                burst,
                burstResult.Receipt));
            events["server-revision-burst"] = await WaitForAppliedEventAsync(
                cluster.Server,
                burstResult.Receipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            Dictionary<string, StateSnapshotV2> converged = await WaitForRevisionOnAllAsync(
                initialNodes,
                fixture.ProbeCell,
                burstResult.FinalValue,
                burstResult.FinalRevision,
                requireStaleEvidenceOnClients: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            foreach ((string role, StateSnapshotV2 snapshot) in converged) {
                snapshots[$"{role}-revision-final"] = snapshot;
            }
            EnsureRevisionSnapshotsEqual(converged.Values.ToArray());
            long duplicatedPackets = converged.Values
                .Where(snapshot => snapshot.Network != null)
                .Sum(snapshot => snapshot.Network!.FaultDuplicated);
            if (converged["server"].Network?.FaultDuplicated <= 0 || duplicatedPackets <= 0) {
                throw new InvalidOperationException(
                    "The deterministic fault injector did not report duplicated terrain packets.");
            }
            metrics["revision.baseline"] = baselineRevision.ToString(CultureInfo.InvariantCulture);
            metrics["revision.final"] = burstResult.FinalRevision.ToString(CultureInfo.InvariantCulture);
            metrics["fault.duplicatedPackets"] = duplicatedPackets.ToString(CultureInfo.InvariantCulture);
            metrics["fault.profile"] = FormatFaultProfile(faultProfile);
            assertions.Add(new ScenarioAssertionRecordV2(
                "terrain-revision-stale-duplicate-reorder-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"revision={burstResult.FinalRevision}; duplicated={duplicatedPackets}; pending=0"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(Seed: unchecked(faultProfile.Seed + 100)),
                    token),
                scenarioToken).ConfigureAwait(false);

            GameNodeProcess lateClient = cluster.Clients[2];
            NodeStatus lateStatus = await runner.RunAsync(
                "connect-late-client",
                "network.connect(client-c)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        lateClient,
                        "LearnerC",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    return await lateClient.GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"playerIndex={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            await ApplyObserverViewAsync(lateClient, lateStatus.PlayerIndex, fixture, scenarioToken)
                .ConfigureAwait(false);
            snapshots["client-c-after-late-join"] = await WaitForCellAsync(
                lateClient,
                fixture.ProbeCell,
                burstResult.FinalValue,
                burstResult.FinalRevision,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "terrain-streaming-late-join-baseline",
                "passed",
                [lateClient.Role],
                $"revision={burstResult.FinalRevision}; value={burstResult.FinalValue}"));

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
            if (reconnectStatus.PlayerIndex != initialActorPlayerIndex) {
                throw new InvalidOperationException(
                    $"Actor identity changed from {initialActorPlayerIndex} to {reconnectStatus.PlayerIndex}.");
            }
            actorPlayerIndex = reconnectStatus.PlayerIndex;
            await ApplyObserverViewAsync(actorClient, actorPlayerIndex, fixture, scenarioToken)
                .ConfigureAwait(false);
            snapshots["client-a-after-reconnect"] = await WaitForCellAsync(
                actorClient,
                fixture.ProbeCell,
                burstResult.FinalValue,
                burstResult.FinalRevision,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "terrain-streaming-reconnect-baseline",
                "passed",
                [actorClient.Role],
                $"playerIndex={actorPlayerIndex}; revision={burstResult.FinalRevision}"));

            StateSnapshotV2 beforeSave = await WaitForCellAsync(
                cluster.Server,
                fixture.ProbeCell,
                burstResult.FinalValue,
                burstResult.FinalRevision,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-before-save"] = beforeSave;
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

            NodeStatus persistenceStatus = await runner.RunAsync(
                "connect-persistence-observer",
                "network.connect(client-c)",
                async token => {
                    await MultiplayerGameplayBootstrap.ConnectClientAndEnterAsync(
                        cluster,
                        lateClient,
                        "LearnerC",
                        stepTimeout,
                        token).ConfigureAwait(false);
                    return await lateClient.GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"playerIndex={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            await ApplyObserverViewAsync(
                lateClient,
                persistenceStatus.PlayerIndex,
                fixture,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 serverReloaded = await WaitForCellAsync(
                cluster.Server,
                fixture.ProbeCell,
                burstResult.FinalValue,
                expectedRevision: 0,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 clientReloaded = await WaitForCellAsync(
                lateClient,
                fixture.ProbeCell,
                burstResult.FinalValue,
                expectedRevision: 0,
                requireLoaded: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-after-reload"] = serverReloaded;
            snapshots["client-c-after-reload"] = clientReloaded;
            SnapshotV2Diff persistenceDiff = SnapshotV2Comparer.CompareTerrainCells(
                beforeSave,
                serverReloaded);
            if (!persistenceDiff.Equal) {
                throw new InvalidOperationException(
                    "Terrain value changed across save/reload: "
                    + string.Join(", ", persistenceDiff.Differences.Select(item => item.Path)));
            }
            assertions.Add(new ScenarioAssertionRecordV2(
                "terrain-save-reload-revision-boundary",
                "passed",
                [cluster.Server.Role, lateClient.Role],
                $"world={savedWorldDirectory}; value={burstResult.FinalValue}; revisionReset=0"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(cluster, fixture?.ProbeCell, snapshots, events)
                    .ConfigureAwait(false);
                foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
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
            throw new TerrainStreamingMultiplayerScenarioException(
                $"Terrain streaming multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new TerrainStreamingMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new TerrainStreamingMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private static async Task VerifyAuthorityRejectionsAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int actorPlayerIndex,
        CancellationToken cancellationToken) {
        await runner.RunAsync(
            "verify-authority",
            "typed authority rejection",
            async token => {
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<PlayerMoveCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMove,
                        new PlayerMoveCommand(actorPlayerIndex, new Vector3State(0, 0, 0.5)),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[0].SendAsync<RunTerrainRevisionBurstCommand, TerrainRevisionBurstResult>(
                        ControlCommandNames.RunTerrainRevisionBurst,
                        new RunTerrainRevisionBurstCommand(
                            new TerrainCellCoordinate(0, 64, 0),
                            [1, 2]),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<ApplyTerrainStreamingViewCommand, ControlAck>(
                        ControlCommandNames.ApplyTerrainStreamingView,
                        new ApplyTerrainStreamingViewCommand(
                            actorPlayerIndex,
                            new Vector3State(0, 65, 0),
                            StandingValue: 1,
                            VisibilityDistance: 48,
                            ContentDistance: 64),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireRejectionAsync<T>(
        Func<Task<T>> action,
        string expectedCode) {
        try {
            _ = await action().ConfigureAwait(false);
        }
        catch (ControlCommandException exception) when (exception.Code == expectedCode) {
            return;
        }
        throw new InvalidOperationException(
            $"Expected automation command rejection '{expectedCode}'.");
    }

    private static Task<ControlAck> ApplyViewAsync(
        GameNodeProcess node,
        TerrainStreamingFixture fixture,
        bool actor,
        bool freezeBody,
        bool armMove,
        CancellationToken cancellationToken) =>
        node.SendAsync<ApplyTerrainStreamingViewCommand, ControlAck>(
            ControlCommandNames.ApplyTerrainStreamingView,
            new ApplyTerrainStreamingViewCommand(
                actor ? fixture.ActorPlayerIndex : fixture.ObserverPlayerIndex,
                actor ? fixture.ActorPosition : fixture.ObserverPosition,
                fixture.StandingValue,
                fixture.VisibilityDistance,
                fixture.ContentDistance,
                freezeBody,
                armMove),
            cancellationToken: cancellationToken);

    private static Task<ControlAck> ApplyObserverViewAsync(
        GameNodeProcess node,
        int playerIndex,
        TerrainStreamingFixture fixture,
        CancellationToken cancellationToken) =>
        node.SendAsync<ApplyTerrainStreamingViewCommand, ControlAck>(
            ControlCommandNames.ApplyTerrainStreamingView,
            new ApplyTerrainStreamingViewCommand(
                playerIndex,
                fixture.ObserverPosition,
                fixture.StandingValue,
                fixture.VisibilityDistance,
                fixture.ContentDistance,
                FreezeBody: true),
            cancellationToken: cancellationToken);

    private static async Task<StateSnapshotV2> WaitForCellAsync(
        GameNodeProcess node,
        TerrainCellCoordinate coordinate,
        int? expectedValue,
        long? expectedRevision,
        bool requireLoaded,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureTerrainAsync(node, coordinate, cancellationToken)
                .ConfigureAwait(false);
            TerrainCellSnapshotV2 cell = FindCell(last, coordinate);
            bool loadedMatches = requireLoaded ? cell.IsLoaded : !cell.IsLoaded;
            if (loadedMatches
                && (!expectedValue.HasValue || cell.Value == expectedValue.Value)
                && (!expectedRevision.HasValue || cell.Revision == expectedRevision.Value)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        TerrainCellSnapshotV2? actual = last == null ? null : FindCell(last, coordinate);
        throw new TimeoutException(
            $"Node '{node.Role}' did not reach terrain cell expectation at {FormatCell(coordinate)}. "
            + $"ExpectedLoaded={requireLoaded}; ExpectedValue={expectedValue}; "
            + $"ExpectedRevision={expectedRevision}; Actual={actual}.");
    }

    private static async Task<StateSnapshotV2> WaitForPlayerReadyAsync(
        GameNodeProcess node,
        int playerIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                ControlCommandNames.GetSnapshotV2,
                new GetSnapshotV2Command(
                    [SnapshotDomain.Players],
                    PlayerIndexes: [playerIndex]),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (last.Players.SingleOrDefault()?.ReadyForPlaying == true
                && last.Players[0].Body != null) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not observe ready player {playerIndex}. Last={last}.");
    }

    private static async Task<StateSnapshotV2> WaitForServerStreamingLocationAsync(
        GameNodeProcess server,
        TerrainStreamingFixture fixture,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        double targetCenterX = (fixture.ProbeCell.X >> 4) * 16 + 8;
        double targetCenterZ = (fixture.ProbeCell.Z >> 4) * 16 + 8;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureTerrainAsync(server, fixture.ProbeCell, cancellationToken)
                .ConfigureAwait(false);
            TerrainUpdateLocationSnapshotV2? location = last.TerrainStreaming?.Locations
                .SingleOrDefault(item => item.PlayerIndex == fixture.ActorPlayerIndex);
            double distance = location == null
                ? double.PositiveInfinity
                : Math.Sqrt(
                    Math.Pow(location.Center.X - targetCenterX, 2)
                    + Math.Pow(location.Center.Y - targetCenterZ, 2));
            if (location != null
                && distance <= fixture.ContentDistance
                && FindCell(last, fixture.ProbeCell).IsLoaded) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Server did not observe actor {fixture.ActorPlayerIndex} crossing the streaming boundary. "
            + $"LastStreaming={last?.TerrainStreaming}.");
    }

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForRevisionOnAllAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        TerrainCellCoordinate coordinate,
        int value,
        long revision,
        bool requireStaleEvidenceOnClients,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        GameNodeProcess[] ordered = nodes.ToArray();
        StateSnapshotV2[] captured = await Task.WhenAll(ordered.Select(node =>
            WaitForRevisionAsync(
                node,
                coordinate,
                value,
                revision,
                requireStaleEvidenceOnClients && node.Role.StartsWith("client", StringComparison.Ordinal),
                timeout,
                cancellationToken))).ConfigureAwait(false);
        return ordered
            .Select((node, index) => (node.Role, Snapshot: captured[index]))
            .ToDictionary(item => item.Role, item => item.Snapshot, StringComparer.Ordinal);
    }

    private static async Task<StateSnapshotV2> WaitForRevisionAsync(
        GameNodeProcess node,
        TerrainCellCoordinate coordinate,
        int value,
        long revision,
        bool requireStaleEvidence,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureTerrainAsync(node, coordinate, cancellationToken)
                .ConfigureAwait(false);
            TerrainCellSnapshotV2 cell = FindCell(last, coordinate);
            TerrainStreamingSnapshotV2? streaming = last.TerrainStreaming;
            bool metricsMatch = streaming != null
                && streaming.BufferOverflows == 0
                && streaming.PendingRevisionChanges == 0
                && (!requireStaleEvidence || streaming.IgnoredStalePackets > 0);
            if (cell.IsLoaded
                && cell.Value == value
                && cell.Revision == revision
                && metricsMatch) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge revision {revision} at {FormatCell(coordinate)}. "
            + $"Last={last?.TerrainCells.SingleOrDefault()}; Streaming={last?.TerrainStreaming}.");
    }

    private static Task<StateSnapshotV2> CaptureTerrainAsync(
        GameNodeProcess node,
        TerrainCellCoordinate coordinate,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.Terrain],
                TerrainCells: [coordinate]),
            cancellationToken: cancellationToken);

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
            ActionEventRecord[] selected = last.Events
                .Where(item => string.Equals(item.ActionId, actionId, StringComparison.Ordinal))
                .ToArray();
            ActionEventRecord? rejected = selected.FirstOrDefault(item =>
                item.Phase == ActionEventPhase.Rejected);
            if (rejected != null) {
                throw new InvalidOperationException(
                    $"Action '{actionId}' was rejected: {rejected.Code}.");
            }
            if (selected.Any(item => item.Phase == ActionEventPhase.Queued)
                && selected.Any(item => item.Phase == ActionEventPhase.Applied)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not publish queued/applied events for '{actionId}'. Last={last}.");
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
                    throw new InvalidOperationException("Server declined to save the streaming world.");
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
                    "Saved streaming world did not become playable.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return savedWorldDirectory;
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        TerrainCellCoordinate? coordinate,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            if (coordinate != null) {
                try {
                    snapshots[$"{node.Role}-final"] = await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                        ControlCommandNames.GetSnapshotV2,
                        new GetSnapshotV2Command(
                            [SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.Terrain, SnapshotDomain.Events],
                            TerrainCells: [coordinate],
                            EventLimit: AutomationProtocol.MaximumEventQueryCount),
                        timeout: TimeSpan.FromSeconds(5),
                        cancellationToken: CancellationToken.None).ConfigureAwait(false);
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

    private static void EnsureFixture(
        TerrainStreamingFixture fixture,
        int actorPlayerIndex,
        int observerPlayerIndex) {
        if (fixture.ActorPlayerIndex != actorPlayerIndex
            || fixture.ObserverPlayerIndex != observerPlayerIndex
            || fixture.ActorPosition == null
            || fixture.ObserverPosition == null
            || fixture.ProbeCell == null
            || fixture.RevisionValues is not { Length: >= 2 }
            || fixture.MoveInput == null
            || fixture.MoveHoldFrames is < 1 or > 600
            || fixture.ContentDistance < fixture.VisibilityDistance) {
            throw new InvalidOperationException("Terrain streaming fixture is incomplete or inconsistent.");
        }
    }

    private static void EnsureRevisionSnapshotsEqual(StateSnapshotV2[] snapshots) {
        StateSnapshotV2 expected = snapshots[0];
        foreach (StateSnapshotV2 actual in snapshots[1..]) {
            SnapshotV2Diff diff = SnapshotV2Comparer.CompareTerrainCells(
                expected,
                actual,
                new SnapshotV2TerrainComparisonOptions(CompareRevision: true));
            if (!diff.Equal) {
                throw new InvalidOperationException(
                    "Terrain revision snapshots diverged: "
                    + string.Join(", ", diff.Differences.Select(item => item.Path)));
            }
        }
    }

    private static TerrainCellSnapshotV2 FindCell(
        StateSnapshotV2 snapshot,
        TerrainCellCoordinate coordinate) =>
        snapshot.TerrainCells.Single(cell =>
            cell.X == coordinate.X && cell.Y == coordinate.Y && cell.Z == coordinate.Z);

    private static TerrainCellCoordinate ActorStandingCell(TerrainStreamingFixture fixture) =>
        new(
            (int)Math.Floor(fixture.ActorPosition.X),
            (int)Math.Floor(fixture.ActorPosition.Y) - 1,
            (int)Math.Floor(fixture.ActorPosition.Z));

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

    private static void ValidateReplayPlan(TerrainStreamingReplayPlan? replayPlan) {
        if (replayPlan == null) {
            return;
        }
        if (replayPlan.Move.Input == null
            || replayPlan.Move.HoldFrames is < 1 or > 600
            || replayPlan.RevisionBurst.Cell == null
            || replayPlan.RevisionBurst.Values is not { Length: >= 2 and <= 64 }
            || !replayPlan.RevisionBurst.SendStaleSnapshot) {
            throw new InvalidDataException("Terrain streaming replay actions are invalid.");
        }
    }

    private static string FormatFaultProfile(NetworkFaultProfile profile) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"seed={profile.Seed}; latencyMs={profile.BaseLatencyMilliseconds}; jitterMs={profile.JitterMilliseconds}; loss={profile.LossRate:R}; duplicate={profile.DuplicationRate:R}; reorder={profile.ReorderRate:R}; reorderDelayMs={profile.ReorderDelayMilliseconds}; bandwidth={profile.BandwidthBytesPerSecond}");

    private static string FormatCell(TerrainCellCoordinate cell) =>
        $"{cell.X},{cell.Y},{cell.Z}";

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
