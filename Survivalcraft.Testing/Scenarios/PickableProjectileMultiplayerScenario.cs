using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record PickableProjectileReplayPlan(
    AttractPickableCommand Attraction,
    PlayerFireProjectileCommand FireProjectile,
    RunPickableLifecycleBurstCommand LifecycleBurst);

public sealed record PickableProjectileMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    PickableProjectileReplayPlan? ReplayPlan = null);

public sealed record PickableProjectileMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class PickableProjectileMultiplayerScenarioException : Exception {
    public PickableProjectileMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class PickableProjectileMultiplayerScenario {
    public const string ScenarioName = "pickable-projectile-multiplayer";
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
        new("prepare-pickable-projectile-fixture", ControlCommandNames.PreparePickableProjectileFixture),
        new("apply-actor-fixture", ControlCommandNames.ApplyPickableProjectileFixture),
        new("verify-initial-pickable", ControlCommandNames.GetSnapshotV2),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("attract-pickable", ControlCommandNames.AttractPickable),
        new("verify-attraction-convergence", ControlCommandNames.GetSnapshotV2),
        new("fire-projectile", ControlCommandNames.PlayerFireProjectile),
        new("verify-projectile-deduplication", ControlCommandNames.GetSnapshotV2),
        new("run-pickable-lifecycle-burst", ControlCommandNames.RunPickableLifecycleBurst),
        new("verify-pickable-tombstone", ControlCommandNames.GetSnapshotV2),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("verify-fault-queue-drained", ControlCommandNames.GetSnapshotV2),
        new("connect-late-client", "network.connect(client-c)"),
        new("verify-late-join-baseline", ControlCommandNames.GetSnapshotV2),
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
            Seed: unchecked(seed + 863),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18);

    public static PickableProjectileReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != 3) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly three recorded actions.");
        }
        RequireReplayAction(records[0], "server", ControlCommandNames.AttractPickable, 0);
        RequireReplayAction(records[1], "client-a", ControlCommandNames.PlayerFireProjectile, 1);
        RequireReplayAction(records[2], "server", ControlCommandNames.RunPickableLifecycleBurst, 2);
        PickableProjectileReplayPlan plan = new(
            DeserializeAction<AttractPickableCommand>(records[0]),
            DeserializeAction<PlayerFireProjectileCommand>(records[1]),
            DeserializeAction<RunPickableLifecycleBurstCommand>(records[2]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<PickableProjectileMultiplayerScenarioResult> RunAsync(
        PickableProjectileMultiplayerScenarioOptions options,
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
                "Pickable lifecycle and projectile deduplication require a non-empty network fault profile.",
                nameof(options.NetworkFaults));
        }

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(10);
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
        List<int> observedPickableIds = [];
        Stopwatch stopwatch = Stopwatch.StartNew();
        MultiplayerCluster? cluster = null;
        PickableProjectileFixture? fixture = null;
        PickableLifecycleBurstResult? burstResult = null;
        long projectileEventId = 0;
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
                    ControlCommandNames.PreparePickableProjectileFixture,
                    ControlCommandNames.ApplyPickableProjectileFixture,
                    ControlCommandNames.AttractPickable,
                    ControlCommandNames.PlayerFireProjectile,
                    ControlCommandNames.RunPickableLifecycleBurst
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Pickable,
                    AutomationCommandDomain.Projectile
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"PickableProjectile-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            int initialActorPlayerIndex = actorPlayerIndex;
            GameNodeProcess actorClient = cluster.Clients[0];
            GameNodeProcess observerClient = cluster.Clients[1];
            GameNodeProcess[] initialNodes = [cluster.Server, actorClient, observerClient];
            metrics["actor.initialPlayerIndex"] = actorPlayerIndex.ToString(CultureInfo.InvariantCulture);

            await VerifyAuthorityRejectionsAsync(cluster, runner, actorPlayerIndex, scenarioToken)
                .ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "pickable-projectile-authority-enforcement",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "only the server can prepare/mutate pickables and only a client can originate projectile input"));

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
                "prepare-pickable-projectile-fixture",
                ControlCommandNames.PreparePickableProjectileFixture,
                token => cluster.Server.SendAsync<PreparePickableProjectileFixtureCommand, PickableProjectileFixture>(
                    ControlCommandNames.PreparePickableProjectileFixture,
                    new PreparePickableProjectileFixtureCommand(actorPlayerIndex),
                    timeout: stepTimeout,
                    cancellationToken: token),
                result => $"pickable={result.PersistentPickableId}@{result.PersistentRevision}; inventory={result.ActorInventoryId}",
                scenarioToken).ConfigureAwait(false);
            EnsureFixture(fixture);
            observedPickableIds.Add(fixture.PersistentPickableId);

            await runner.RunAsync(
                "apply-actor-fixture",
                ControlCommandNames.ApplyPickableProjectileFixture,
                token => ApplyActorFixtureWhenReadyAsync(actorClient, fixture, stepTimeout, token),
                scenarioToken).ConfigureAwait(false);

            StateSnapshotV2[] initialSnapshots = await Task.WhenAll(initialNodes.Select(node =>
                WaitForPickableAsync(
                    node,
                    fixture.PersistentPickableId,
                    expectedExists: true,
                    expectedValue: fixture.PersistentValue,
                    expectedCount: fixture.PersistentCount,
                    minimumRevision: fixture.PersistentRevision,
                    requireTombstone: false,
                    requireStaleEvidence: false,
                    minimumMovement: null,
                    origin: null,
                    stepTimeout,
                    scenarioToken))).ConfigureAwait(false);
            AddSnapshots(snapshots, "initial", initialSnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "pickable-live-create-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"pickableId={fixture.PersistentPickableId}; value={fixture.PersistentValue}; count={fixture.PersistentCount}"));

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            AttractPickableCommand attraction = options.ReplayPlan?.Attraction
                ?? new AttractPickableCommand(
                    fixture.PersistentAlias,
                    fixture.PersistentPosition with { Z = fixture.PersistentPosition.Z + 2 });
            ActionReceipt attractionReceipt = await runner.RunAsync(
                "attract-pickable",
                ControlCommandNames.AttractPickable,
                token => cluster.Server.SendAsync<AttractPickableCommand, ActionReceipt>(
                    ControlCommandNames.AttractPickable,
                    attraction,
                    cancellationToken: token),
                result => $"actionId={result.ActionId}; code={result.Code}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(attractionReceipt);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.AttractPickable,
                attraction,
                attractionReceipt));
            events["server-attraction"] = await WaitForAppliedEventAsync(
                cluster.Server,
                attractionReceipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            StateSnapshotV2[] attractedSnapshots = await Task.WhenAll(initialNodes.Select(node =>
                WaitForPickableAsync(
                    node,
                    fixture.PersistentPickableId,
                    expectedExists: true,
                    expectedValue: fixture.PersistentValue,
                    expectedCount: fixture.PersistentCount,
                    minimumRevision: checked(fixture.PersistentRevision + 1),
                    requireTombstone: false,
                    requireStaleEvidence: false,
                    minimumMovement: 0.5,
                    origin: fixture.PersistentPosition,
                    stepTimeout,
                    scenarioToken))).ConfigureAwait(false);
            AddSnapshots(snapshots, "after-attraction", attractedSnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "pickable-attraction-production-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"pickableId={fixture.PersistentPickableId}; productionEntry={attractionReceipt.ProductionEntry}"));

            PlayerFireProjectileCommand fire = options.ReplayPlan?.FireProjectile
                ?? new PlayerFireProjectileCommand(actorPlayerIndex);
            ActionReceipt fireReceipt = await runner.RunAsync(
                "fire-projectile",
                ControlCommandNames.PlayerFireProjectile,
                token => actorClient.SendAsync<PlayerFireProjectileCommand, ActionReceipt>(
                    ControlCommandNames.PlayerFireProjectile,
                    fire,
                    cancellationToken: token),
                result => $"actionId={result.ActionId}; code={result.Code}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(fireReceipt);
            actions.Add(ActionRecord(
                actorClient.Role,
                ControlCommandNames.PlayerFireProjectile,
                fire,
                fireReceipt));
            events["client-a-projectile-input"] = await WaitForAppliedEventAsync(
                actorClient,
                fireReceipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            StateSnapshotV2 serverProjectile = await WaitForGeneratedProjectileAsync(
                cluster.Server,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            projectileEventId = serverProjectile.ProjectileEvents?.HighestGeneratedEventId ?? 0;
            if (projectileEventId <= 0) {
                throw new InvalidOperationException("The server did not assign a projectile event id.");
            }
            snapshots["server-projectile-generated"] = serverProjectile;
            StateSnapshotV2[] projectileObservers = await Task.WhenAll(
                new[] { actorClient, observerClient }.Select(node =>
                    WaitForAcceptedProjectileAsync(
                        node,
                        projectileEventId,
                        requireDuplicateEvidence: true,
                        stepTimeout,
                        scenarioToken))).ConfigureAwait(false);
            AddSnapshots(
                snapshots,
                "projectile-observed",
                projectileObservers,
                actorClient,
                observerClient);
            assertions.Add(new ScenarioAssertionRecordV2(
                "projectile-production-input-and-deduplication",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"eventId={projectileEventId}; both clients accepted once and rejected deterministic duplicates"));

            RunPickableLifecycleBurstCommand burst = options.ReplayPlan?.LifecycleBurst
                ?? new RunPickableLifecycleBurstCommand();
            burstResult = await runner.RunAsync(
                "run-pickable-lifecycle-burst",
                ControlCommandNames.RunPickableLifecycleBurst,
                token => cluster.Server.SendAsync<RunPickableLifecycleBurstCommand, PickableLifecycleBurstResult>(
                    ControlCommandNames.RunPickableLifecycleBurst,
                    burst,
                    cancellationToken: token),
                result => $"tombstone={result.TombstonePickableId}@{result.FinalRemovalRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(burstResult.Receipt);
            ValidateBurstResult(burstResult);
            observedPickableIds.Add(burstResult.TombstonePickableId);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.RunPickableLifecycleBurst,
                burst,
                burstResult.Receipt));
            events["server-pickable-lifecycle"] = await WaitForAppliedEventAsync(
                cluster.Server,
                burstResult.Receipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            Dictionary<string, StateSnapshotV2> lifecycleSnapshots = new(StringComparer.Ordinal);
            foreach (GameNodeProcess node in initialNodes) {
                lifecycleSnapshots[node.Role] = await WaitForPickableAsync(
                    node,
                    burstResult.TombstonePickableId,
                    expectedExists: false,
                    expectedValue: null,
                    expectedCount: null,
                    minimumRevision: burstResult.FinalRemovalRevision,
                    requireTombstone: true,
                    requireStaleEvidence: node != cluster.Server,
                    minimumMovement: null,
                    origin: null,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
                snapshots[$"{node.Role}-lifecycle-final"] = lifecycleSnapshots[node.Role];
            }
            long duplicatedPackets = lifecycleSnapshots.Values
                .Where(snapshot => snapshot.Network != null)
                .Sum(snapshot => snapshot.Network!.FaultDuplicated);
            if (duplicatedPackets <= 0
                || lifecycleSnapshots.Values
                    .Where(snapshot => snapshot.Node.Role.StartsWith("client", StringComparison.Ordinal))
                    .Any(snapshot => snapshot.PickableLifecycle?.IgnoredStalePackets <= 0)) {
                throw new InvalidOperationException(
                    "The mutation sentry did not observe duplicated traffic and stale-create rejection on every client.");
            }
            metrics["fault.duplicatedPackets"] = duplicatedPackets.ToString(CultureInfo.InvariantCulture);
            metrics["fault.profile"] = FormatFaultProfile(faultProfile);
            metrics["pickable.tombstoneRevision"] = burstResult.FinalRemovalRevision.ToString(CultureInfo.InvariantCulture);
            metrics["projectile.eventId"] = projectileEventId.ToString(CultureInfo.InvariantCulture);
            assertions.Add(new ScenarioAssertionRecordV2(
                "pickable-stale-create-resurrection-sentry",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"pickableId={burstResult.TombstonePickableId}; removalRevision>={burstResult.FinalRemovalRevision}; duplicated={duplicatedPackets}"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(Seed: unchecked(faultProfile.Seed + 100)),
                    token),
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "verify-fault-queue-drained",
                ControlCommandNames.GetSnapshotV2,
                token => WaitForFaultQueuesDrainedAsync(initialNodes, stepTimeout, token),
                scenarioToken).ConfigureAwait(false);

            GameNodeProcess lateClient = cluster.Clients[2];
            _ = await runner.RunAsync(
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
            StateSnapshotV2 lateSnapshot = await WaitForCurrentBaselineAsync(
                lateClient,
                fixture,
                burstResult,
                projectileEventId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["client-c-after-late-join"] = lateSnapshot;
            assertions.Add(new ScenarioAssertionRecordV2(
                "pickable-projectile-late-join-baseline",
                "passed",
                [lateClient.Role],
                "live pickable synchronized; the removed lifecycle revision and historical projectile event were not replayed"));

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
            snapshots["client-a-after-reconnect"] = await WaitForCurrentBaselineAsync(
                actorClient,
                fixture,
                burstResult,
                projectileEventId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "pickable-projectile-reconnect-baseline",
                "passed",
                [actorClient.Role],
                $"stablePlayerIndex={actorPlayerIndex}; historical transient state was not replayed"));

            StateSnapshotV2 beforeSave = await CapturePickablesProjectilesAsync(
                cluster.Server,
                observedPickableIds,
                projectileEventId,
                scenarioToken).ConfigureAwait(false);
            PickableSnapshotV2 persistentBeforeSave = FindPickable(
                beforeSave,
                fixture.PersistentPickableId);
            if (!persistentBeforeSave.Exists) {
                throw new InvalidOperationException("The persistent pickable disappeared before save.");
            }
            PickableSnapshotV2[] livePickablesBeforeSave = (beforeSave.Pickables ?? [])
                .Where(pickable => pickable.Exists)
                .ToArray();
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
            _ = await runner.RunAsync(
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

            StateSnapshotV2 serverReloaded = await WaitForPersistedBaselineAsync(
                cluster.Server,
                fixture,
                burstResult,
                livePickablesBeforeSave,
                projectileEventId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 clientReloaded = await WaitForPersistedBaselineAsync(
                lateClient,
                fixture,
                burstResult,
                livePickablesBeforeSave,
                projectileEventId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-after-reload"] = serverReloaded;
            snapshots["client-c-after-reload"] = clientReloaded;
            assertions.Add(new ScenarioAssertionRecordV2(
                "pickable-save-reload-and-projectile-transience",
                "passed",
                [cluster.Server.Role, lateClient.Role],
                $"world={savedWorldDirectory}; livePickables={livePickablesBeforeSave.Length}; tombstone/projectile history reset safely"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(
                    cluster,
                    observedPickableIds,
                    projectileEventId,
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
            throw new PickableProjectileMultiplayerScenarioException(
                $"Pickable/projectile multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new PickableProjectileMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new PickableProjectileMultiplayerScenarioResult(
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
                    () => cluster.Clients[0].SendAsync<PreparePickableProjectileFixtureCommand, PickableProjectileFixture>(
                        ControlCommandNames.PreparePickableProjectileFixture,
                        new PreparePickableProjectileFixtureCommand(actorPlayerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<AttractPickableCommand, ActionReceipt>(
                        ControlCommandNames.AttractPickable,
                        new AttractPickableCommand("persistent", new Vector3State(1, 1, 1)),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<PlayerFireProjectileCommand, ActionReceipt>(
                        ControlCommandNames.PlayerFireProjectile,
                        new PlayerFireProjectileCommand(actorPlayerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[0].SendAsync<RunPickableLifecycleBurstCommand, PickableLifecycleBurstResult>(
                        ControlCommandNames.RunPickableLifecycleBurst,
                        new RunPickableLifecycleBurstCommand(),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
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

    private static async Task ApplyActorFixtureWhenReadyAsync(
        GameNodeProcess actor,
        PickableProjectileFixture fixture,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        TimeSpan evidenceMargin = timeout > TimeSpan.FromSeconds(2)
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromMilliseconds(100);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout - evidenceMargin;
        ControlCommandException? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                _ = await actor.SendAsync<ApplyPickableProjectileFixtureCommand, ControlAck>(
                    ControlCommandNames.ApplyPickableProjectileFixture,
                    new ApplyPickableProjectileFixtureCommand(fixture),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ControlCommandException exception)
                when (string.Equals(exception.Code, "fixture_mismatch", StringComparison.Ordinal)) {
                last = exception;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Client A did not receive the authoritative loaded-crossbow fixture. Last={last?.Message}");
    }

    private static async Task<StateSnapshotV2> WaitForPickableAsync(
        GameNodeProcess node,
        int pickableId,
        bool expectedExists,
        int? expectedValue,
        int? expectedCount,
        long? minimumRevision,
        bool requireTombstone,
        bool requireStaleEvidence,
        double? minimumMovement,
        Vector3State? origin,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CapturePickablesProjectilesAsync(
                node,
                [pickableId],
                projectileEventId: 0,
                cancellationToken).ConfigureAwait(false);
            PickableSnapshotV2 pickable = FindPickable(last, pickableId);
            bool moved = !minimumMovement.HasValue
                || origin != null && Distance(pickable.Position, origin) >= minimumMovement.Value;
            bool matches = pickable.Exists == expectedExists
                && (!expectedValue.HasValue || pickable.Value == expectedValue.Value)
                && (!expectedCount.HasValue || pickable.Count == expectedCount.Value)
                && (!minimumRevision.HasValue || pickable.Revision >= minimumRevision.Value)
                && (!requireTombstone || pickable.IsTombstone)
                && (!requireStaleEvidence || last.PickableLifecycle?.IgnoredStalePackets > 0)
                && moved;
            if (matches) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge pickable {pickableId}. "
            + $"ExpectedExists={expectedExists}; MinimumRevision={minimumRevision}; "
            + $"Last={last?.Pickables?.SingleOrDefault()}; Lifecycle={last?.PickableLifecycle}.");
    }

    private static async Task<StateSnapshotV2> WaitForGeneratedProjectileAsync(
        GameNodeProcess server,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CapturePickablesProjectilesAsync(
                server,
                [],
                projectileEventId: 0,
                cancellationToken).ConfigureAwait(false);
            if (last.ProjectileEvents is {
                    GeneratedEvents: > 0,
                    HighestGeneratedEventId: > 0
                }) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Server did not generate a projectile through the production firing path. Last={last?.ProjectileEvents}.");
    }

    private static async Task<StateSnapshotV2> WaitForAcceptedProjectileAsync(
        GameNodeProcess node,
        long eventId,
        bool requireDuplicateEvidence,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CapturePickablesProjectilesAsync(
                node,
                [],
                eventId,
                cancellationToken).ConfigureAwait(false);
            ProjectileEventSnapshotV2? metrics = last.ProjectileEvents;
            if (metrics != null
                && metrics.AcceptedEvents > 0
                && metrics.HighestAcceptedEventId >= eventId
                && (!requireDuplicateEvidence || metrics.IgnoredDuplicateEvents > 0)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not accept projectile {eventId} and reject its duplicate. Last={last?.ProjectileEvents}.");
    }

    private static async Task<StateSnapshotV2> WaitForCurrentBaselineAsync(
        GameNodeProcess node,
        PickableProjectileFixture fixture,
        PickableLifecycleBurstResult burst,
        long historicalProjectileEventId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CapturePickablesProjectilesAsync(
                node,
                [fixture.PersistentPickableId, burst.TombstonePickableId],
                historicalProjectileEventId,
                cancellationToken).ConfigureAwait(false);
            PickableSnapshotV2 persistent = FindPickable(last, fixture.PersistentPickableId);
            PickableSnapshotV2 removed = FindPickable(last, burst.TombstonePickableId);
            if (persistent.Exists
                && persistent.Value == fixture.PersistentValue
                && persistent.Count == fixture.PersistentCount
                && IsRemovedLifecycleAbsentOrSuperseded(removed, burst)
                && last.ProjectileEvents is { AcceptedEvents: 0, HighestAcceptedEventId: 0 }
                && (last.Projectiles?.Length ?? 0) == 0) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose a clean late/reconnect baseline. "
            + $"Pickables={FormatPickables(last)}; Projectiles={last?.ProjectileEvents}.");
    }

    private static async Task<StateSnapshotV2> WaitForPersistedBaselineAsync(
        GameNodeProcess node,
        PickableProjectileFixture fixture,
        PickableLifecycleBurstResult burst,
        IReadOnlyCollection<PickableSnapshotV2> expectedPickables,
        long historicalProjectileEventId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CapturePickablesProjectilesAsync(
                node,
                expectedPickables
                    .Select(pickable => pickable.PickableId)
                    .Append(burst.TombstonePickableId),
                historicalProjectileEventId,
                cancellationToken).ConfigureAwait(false);
            PickableSnapshotV2 persistent = FindPickable(last, fixture.PersistentPickableId);
            PickableSnapshotV2 removed = FindPickable(last, burst.TombstonePickableId);
            if (persistent.Exists
                && expectedPickables.All(expected => {
                    PickableSnapshotV2 actual = FindPickable(last, expected.PickableId);
                    return actual.Exists
                        && actual.Value == expected.Value
                        && actual.Count == expected.Count;
                })
                && (expectedPickables.Any(expected =>
                        expected.PickableId == burst.TombstonePickableId)
                    || !removed.Exists)
                && last.ProjectileEvents is {
                    GeneratedEvents: 0,
                    AcceptedEvents: 0,
                    HighestGeneratedEventId: 0,
                    HighestAcceptedEventId: 0
                }
                && (last.Projectiles?.Length ?? 0) == 0) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose persisted pickable/transient projectile state. "
            + $"Pickables={FormatPickables(last)}; Projectiles={last?.ProjectileEvents}.");
    }

    private static bool IsRemovedLifecycleAbsentOrSuperseded(
        PickableSnapshotV2 current,
        PickableLifecycleBurstResult removedLifecycle) =>
        !current.Exists || current.Revision > removedLifecycle.FinalRemovalRevision;

    private static async Task WaitForFaultQueuesDrainedAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Dictionary<string, int> last = new(StringComparer.Ordinal);
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            StateSnapshotV2[] snapshots = await Task.WhenAll(nodes.Select(node =>
                CapturePickablesProjectilesAsync(node, [], 0, cancellationToken)))
                .ConfigureAwait(false);
            last = snapshots.ToDictionary(
                snapshot => snapshot.Node.Role,
                snapshot => snapshot.Network?.FaultPending ?? -1,
                StringComparer.Ordinal);
            if (last.Values.All(value => value == 0)) {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            "Network fault queues did not drain: "
            + string.Join(", ", last.Select(item => $"{item.Key}={item.Value}")));
    }

    private static Task<StateSnapshotV2> CapturePickablesProjectilesAsync(
        GameNodeProcess node,
        IEnumerable<int> pickableIds,
        long projectileEventId,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Pickables, SnapshotDomain.Projectiles],
                PickableIds: pickableIds.Distinct().ToArray(),
                ProjectileEventIds: projectileEventId > 0 ? [projectileEventId] : []),
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
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not publish queued and terminal events for '{actionId}'. Last={last}.");
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
                    throw new InvalidOperationException("Server declined to save the pickable/projectile world.");
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
                    "Saved pickable/projectile world did not become playable.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return savedWorldDirectory;
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        IReadOnlyCollection<int> pickableIds,
        long projectileEventId,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            try {
                GetSnapshotV2Command command = new(
                        [
                            SnapshotDomain.Network,
                            SnapshotDomain.Pickables,
                            SnapshotDomain.Projectiles,
                            SnapshotDomain.Events
                        ],
                        EventLimit: AutomationProtocol.MaximumEventQueryCount,
                        PickableIds: pickableIds.Distinct().ToArray(),
                        ProjectileEventIds: projectileEventId > 0 ? [projectileEventId] : []);
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
                // Preserve the original scenario failure and retained process logs.
            }
            try {
                events[$"{node.Role}-final"] = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                    ControlCommandNames.GetEvents,
                    new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                    timeout: TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // A disconnected or failed node can still contribute retained logs.
            }
        }
    }

    private static void EnsureFixture(PickableProjectileFixture fixture) {
        if (fixture.ActorPlayerIndex < 0
            || fixture.ActorEntityId <= 0
            || fixture.ActorInventoryId <= 0
            || fixture.PersistentPickableId <= 0
            || fixture.PersistentValue == 0
            || fixture.PersistentCount <= 0
            || fixture.PersistentRevision <= 0
            || fixture.LoadedCrossbowValue == 0
            || fixture.LoadedCrossbowCount <= 0
            || !string.Equals(fixture.PersistentAlias, "persistent", StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                "Pickable/projectile fixture is incomplete or inconsistent.");
        }
    }

    private static void ValidateBurstResult(PickableLifecycleBurstResult burst) {
        if (burst.TombstonePickableId <= 0
            || burst.Value == 0
            || burst.StaleCreateRevision <= 0
            || burst.FinalRemovalRevision <= burst.StaleCreateRevision
            || !string.Equals(burst.TombstoneAlias, "tombstone", StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Pickable lifecycle burst returned invalid revisions: {burst}.");
        }
    }

    private static PickableSnapshotV2 FindPickable(
        StateSnapshotV2 snapshot,
        int pickableId) =>
        snapshot.Pickables?.Single(item => item.PickableId == pickableId)
        ?? throw new InvalidOperationException(
            $"Snapshot from '{snapshot.Node.Role}' omitted selected pickable {pickableId}.");

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

    private static void RequireReplayAction(
        ScenarioActionRecordV2 record,
        string role,
        string command,
        int index) {
        if (!string.Equals(record.Role, role, StringComparison.Ordinal)
            || !string.Equals(record.Command, command, StringComparison.Ordinal)) {
            throw new InvalidDataException(
                $"Replay action {index} must be {role}/{command}.");
        }
    }

    private static void EnsureAccepted(ActionReceipt receipt) {
        if (!receipt.Accepted) {
            throw new InvalidOperationException(
                $"Action '{receipt.ActionId}' was rejected: {receipt.Code}.");
        }
    }

    private static void ValidateReplayPlan(PickableProjectileReplayPlan? replayPlan) {
        if (replayPlan == null) {
            return;
        }
        if (!string.Equals(replayPlan.Attraction.Alias, "persistent", StringComparison.Ordinal)
            || replayPlan.FireProjectile.PlayerIndex < 0
            || replayPlan.FireProjectile.HoldFrames is < 2 or > 120
            || !string.Equals(replayPlan.LifecycleBurst.TombstoneAlias, "tombstone", StringComparison.Ordinal)
            || !replayPlan.LifecycleBurst.SendStaleCreate) {
            throw new InvalidDataException(
                "Pickable/projectile replay actions are invalid.");
        }
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

    private static double Distance(Vector3State first, Vector3State second) {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        double z = first.Z - second.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    private static string FormatPickables(StateSnapshotV2? snapshot) =>
        snapshot?.Pickables == null
            ? "<none>"
            : string.Join(" | ", snapshot.Pickables.Select(item => item.ToString()));

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
