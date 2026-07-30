using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record EntityCreatureReplayPlan(
    ApplyEntityImpulseCommand Impulse,
    RunEntityLifecycleBurstCommand LifecycleBurst);

public sealed record EntityCreatureMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    EntityCreatureReplayPlan? ReplayPlan = null);

public sealed record EntityCreatureMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class EntityCreatureMultiplayerScenarioException : Exception {
    public EntityCreatureMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class EntityCreatureMultiplayerScenario {
    public const string ScenarioName = "entity-creature-multiplayer";
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
        new("prepare-entity-fixture", ControlCommandNames.PrepareEntityLifecycleFixture),
        new("verify-initial-entity", ControlCommandNames.GetSnapshotV2),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("apply-entity-impulse", ControlCommandNames.ApplyEntityImpulse),
        new("verify-body-convergence", ControlCommandNames.GetSnapshotV2),
        new("run-lifecycle-burst", ControlCommandNames.RunEntityLifecycleBurst),
        new("verify-lifecycle-convergence", ControlCommandNames.GetSnapshotV2),
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
            Seed: unchecked(seed + 701),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18);

    public static EntityCreatureReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != 2) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly two recorded actions.");
        }
        if (!string.Equals(records[0].Role, "server", StringComparison.Ordinal)
            || !string.Equals(records[0].Command, ControlCommandNames.ApplyEntityImpulse, StringComparison.Ordinal)) {
            throw new InvalidDataException("Replay action 0 must be server/entity.applyImpulse.");
        }
        if (!string.Equals(records[1].Role, "server", StringComparison.Ordinal)
            || !string.Equals(records[1].Command, ControlCommandNames.RunEntityLifecycleBurst, StringComparison.Ordinal)) {
            throw new InvalidDataException("Replay action 1 must be server/entity.runLifecycleBurst.");
        }
        EntityCreatureReplayPlan plan = new(
            DeserializeAction<ApplyEntityImpulseCommand>(records[0]),
            DeserializeAction<RunEntityLifecycleBurstCommand>(records[1]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<EntityCreatureMultiplayerScenarioResult> RunAsync(
        EntityCreatureMultiplayerScenarioOptions options,
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
                "Entity lifecycle resilience requires a non-empty network fault profile.",
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
        List<int> observedEntityIds = [];
        Stopwatch stopwatch = Stopwatch.StartNew();
        MultiplayerCluster? cluster = null;
        EntityLifecycleFixture? fixture = null;
        EntityLifecycleBurstResult? burstResult = null;
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
                    ControlCommandNames.PrepareEntityLifecycleFixture,
                    ControlCommandNames.ApplyEntityImpulse,
                    ControlCommandNames.RunEntityLifecycleBurst
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Entity
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"EntityCreature-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            int initialActorPlayerIndex = actorPlayerIndex;
            metrics["actor.initialPlayerIndex"] = actorPlayerIndex.ToString(CultureInfo.InvariantCulture);

            await VerifyAuthorityRejectionsAsync(cluster, runner, actorPlayerIndex, scenarioToken)
                .ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "entity-lifecycle-authority-enforcement",
                "passed",
                [cluster.Server.Role, cluster.Clients[0].Role, cluster.Clients[1].Role],
                "clients cannot prepare entities, apply server physics, or run lifecycle bursts"));
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
                "prepare-entity-fixture",
                ControlCommandNames.PrepareEntityLifecycleFixture,
                token => cluster.Server.SendAsync<PrepareEntityLifecycleFixtureCommand, EntityLifecycleFixture>(
                    ControlCommandNames.PrepareEntityLifecycleFixture,
                    new PrepareEntityLifecycleFixtureCommand(actorPlayerIndex),
                    timeout: stepTimeout,
                    cancellationToken: token),
                result => $"alias={result.Alias}; id={result.EntityId}; revision={result.Revision}",
                scenarioToken).ConfigureAwait(false);
            EnsureFixture(fixture);
            observedEntityIds.Add(fixture.EntityId);

            StateSnapshotV2[] initialSnapshots = await Task.WhenAll(
                new[] { cluster.Server, cluster.Clients[0], cluster.Clients[1] }
                    .Select(node => WaitForEntityAsync(
                        node,
                        fixture.EntityId,
                        exists: true,
                        fixture.TemplateName,
                        fixture.Revision,
                        expectedTombstone: false,
                        expectedPosition: fixture.Position,
                        requireStaleEvidence: false,
                        stepTimeout,
                        scenarioToken))).ConfigureAwait(false);
            AddSnapshots(snapshots, "initial", initialSnapshots, cluster.Server, cluster.Clients[0], cluster.Clients[1]);
            EnsureEntitySnapshotsEqual(initialSnapshots);
            assertions.Add(new ScenarioAssertionRecordV2(
                "entity-live-spawn-convergence",
                "passed",
                [cluster.Server.Role, cluster.Clients[0].Role, cluster.Clients[1].Role],
                $"entityId={fixture.EntityId}; template={fixture.TemplateName}; revision={fixture.Revision}"));

            GameNodeProcess[] initialNodes = [cluster.Server, cluster.Clients[0], cluster.Clients[1]];
            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            ApplyEntityImpulseCommand impulse = options.ReplayPlan?.Impulse
                ?? new ApplyEntityImpulseCommand(
                    fixture.Alias,
                    new Vector3State(1.5, 0, 0.5),
                    ActiveFrames: 12);
            ActionReceipt impulseReceipt = await runner.RunAsync(
                "apply-entity-impulse",
                ControlCommandNames.ApplyEntityImpulse,
                token => cluster.Server.SendAsync<ApplyEntityImpulseCommand, ActionReceipt>(
                    ControlCommandNames.ApplyEntityImpulse,
                    impulse,
                    cancellationToken: token),
                result => $"actionId={result.ActionId}; code={result.Code}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(impulseReceipt);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.ApplyEntityImpulse,
                impulse,
                impulseReceipt));
            events["server-impulse"] = await WaitForAppliedEventAsync(
                cluster.Server,
                impulseReceipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            StateSnapshotV2 serverMoved = await WaitForMovedEntityAsync(
                cluster.Server,
                fixture,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            EntitySnapshotV2 movedEntity = FindEntity(serverMoved, fixture.EntityId);
            EntityBodySnapshotV2 movedBody = movedEntity.Body
                ?? throw new InvalidOperationException(
                    $"Moved entity {fixture.EntityId} has no body snapshot.");
            StateSnapshotV2[] movedSnapshots = [
                serverMoved,
                await WaitForEntitySnapshotAsync(
                    cluster.Clients[0],
                    serverMoved,
                    fixture.EntityId,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false),
                await WaitForEntitySnapshotAsync(
                    cluster.Clients[1],
                    serverMoved,
                    fixture.EntityId,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false)
            ];
            AddSnapshots(snapshots, "after-impulse", movedSnapshots, initialNodes);
            EnsureEntitySnapshotsEqual(movedSnapshots);
            assertions.Add(new ScenarioAssertionRecordV2(
                "entity-authoritative-body-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"entityId={fixture.EntityId}; position={FormatVector(movedBody.Position)}"));

            RunEntityLifecycleBurstCommand burst = options.ReplayPlan?.LifecycleBurst
                ?? new RunEntityLifecycleBurstCommand();
            burstResult = await runner.RunAsync(
                "run-lifecycle-burst",
                ControlCommandNames.RunEntityLifecycleBurst,
                token => cluster.Server.SendAsync<RunEntityLifecycleBurstCommand, EntityLifecycleBurstResult>(
                    ControlCommandNames.RunEntityLifecycleBurst,
                    burst,
                    cancellationToken: token),
                result => $"replacement={result.Replacement.EntityId}@{result.FinalReplacementRevision}; tombstone={result.TombstoneEntityId}@{result.FinalTombstoneRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(burstResult.Receipt);
            ValidateBurstResult(burstResult);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.RunEntityLifecycleBurst,
                burst,
                burstResult.Receipt));
            events["server-lifecycle-burst"] = await WaitForAppliedEventAsync(
                cluster.Server,
                burstResult.Receipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            observedEntityIds.Add(burstResult.Replacement.EntityId);
            observedEntityIds.Add(burstResult.TombstoneEntityId);

            Dictionary<string, StateSnapshotV2> lifecycleSnapshots = new(StringComparer.Ordinal);
            foreach (GameNodeProcess node in initialNodes) {
                StateSnapshotV2 replacementSnapshot = await WaitForEntityAsync(
                    node,
                    burstResult.Replacement.EntityId,
                    true,
                    burstResult.Replacement.TemplateName,
                    burstResult.FinalReplacementRevision,
                    false,
                    burstResult.Replacement.Position,
                    requireStaleEvidence: node.Role.StartsWith("client", StringComparison.Ordinal),
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
                _ = await WaitForEntityAsync(
                    node,
                    burstResult.TombstoneEntityId,
                    false,
                    expectedTemplate: null,
                    burstResult.FinalTombstoneRevision,
                    expectedTombstone: true,
                    expectedPosition: null,
                    requireStaleEvidence: node.Role.StartsWith("client", StringComparison.Ordinal),
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
                lifecycleSnapshots[node.Role] = await CaptureEntitiesAsync(
                    node,
                    observedEntityIds,
                    scenarioToken).ConfigureAwait(false);
                snapshots[$"{node.Role}-lifecycle-final"] = lifecycleSnapshots[node.Role];
            }
            EnsureEntitySnapshotsEqual(lifecycleSnapshots.Values.ToArray());
            long duplicatedPackets = lifecycleSnapshots.Values
                .Where(snapshot => snapshot.Network != null)
                .Sum(snapshot => snapshot.Network!.FaultDuplicated);
            if (lifecycleSnapshots["server"].Network?.FaultDuplicated <= 0 || duplicatedPackets <= 0) {
                throw new InvalidOperationException(
                    "The deterministic fault injector did not report duplicated entity packets.");
            }
            metrics["fault.duplicatedPackets"] = duplicatedPackets.ToString(CultureInfo.InvariantCulture);
            metrics["fault.profile"] = FormatFaultProfile(faultProfile);
            metrics["replacement.revision"] = burstResult.FinalReplacementRevision.ToString(CultureInfo.InvariantCulture);
            metrics["tombstone.revision"] = burstResult.FinalTombstoneRevision.ToString(CultureInfo.InvariantCulture);
            assertions.Add(new ScenarioAssertionRecordV2(
                "entity-stale-load-duplicate-reorder-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"replacementRevision={burstResult.FinalReplacementRevision}; tombstoneRevision={burstResult.FinalTombstoneRevision}; duplicated={duplicatedPackets}"));

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(Seed: unchecked(faultProfile.Seed + 100)),
                    token),
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
            snapshots["client-c-after-late-join"] = await WaitForCurrentEntitiesAsync(
                lateClient,
                fixture,
                burstResult,
                requireHistoricalTombstone: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "entity-late-join-baseline",
                "passed",
                [lateClient.Role],
                "current entities loaded at authoritative revisions; historically removed entity remains absent"));

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
            snapshots["client-a-after-reconnect"] = await WaitForCurrentEntitiesAsync(
                actorClient,
                fixture,
                burstResult,
                requireHistoricalTombstone: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "entity-reconnect-baseline",
                "passed",
                [actorClient.Role],
                $"stablePlayerIndex={actorPlayerIndex}; survivor={fixture.EntityId}; replacement={burstResult.Replacement.EntityId}"));

            StateSnapshotV2 beforeSave = await CaptureEntitiesAsync(
                cluster.Server,
                observedEntityIds,
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
            StateSnapshotV2 serverReloaded = await WaitForPersistedEntitiesAsync(
                cluster.Server,
                fixture,
                burstResult,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 clientReloaded = await WaitForPersistedEntitiesAsync(
                lateClient,
                fixture,
                burstResult,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-after-reload"] = serverReloaded;
            snapshots["client-c-after-reload"] = clientReloaded;
            EnsureEntityExistenceEqual(serverReloaded, clientReloaded, observedEntityIds);
            assertions.Add(new ScenarioAssertionRecordV2(
                "entity-save-reload-lifecycle-boundary",
                "passed",
                [cluster.Server.Role, lateClient.Role],
                $"world={savedWorldDirectory}; survivor/replacement persisted; tombstone history reset but removed entity stayed absent"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(cluster, observedEntityIds, snapshots, events)
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
            throw new EntityCreatureMultiplayerScenarioException(
                $"Entity creature multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new EntityCreatureMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new EntityCreatureMultiplayerScenarioResult(
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
                    () => cluster.Clients[0].SendAsync<PrepareEntityLifecycleFixtureCommand, EntityLifecycleFixture>(
                        ControlCommandNames.PrepareEntityLifecycleFixture,
                        new PrepareEntityLifecycleFixtureCommand(actorPlayerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[0].SendAsync<ApplyEntityImpulseCommand, ActionReceipt>(
                        ControlCommandNames.ApplyEntityImpulse,
                        new ApplyEntityImpulseCommand("survivor", new Vector3State(1, 0, 0)),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunEntityLifecycleBurstCommand, EntityLifecycleBurstResult>(
                        ControlCommandNames.RunEntityLifecycleBurst,
                        new RunEntityLifecycleBurstCommand(),
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

    private static async Task<StateSnapshotV2> WaitForMovedEntityAsync(
        GameNodeProcess node,
        EntityLifecycleFixture fixture,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureEntitiesAsync(node, [fixture.EntityId], cancellationToken)
                .ConfigureAwait(false);
            EntitySnapshotV2 entity = FindEntity(last, fixture.EntityId);
            if (entity.Exists
                && entity.Revision == fixture.Revision
                && entity.Body != null
                && Distance(entity.Body.Position, fixture.Position) >= 0.05
                && Length(entity.Body.Velocity) <= 0.1) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose a settled moved entity {fixture.EntityId}. Last={last?.Entities?.SingleOrDefault()}.");
    }

    private static async Task<StateSnapshotV2> WaitForEntityAsync(
        GameNodeProcess node,
        int entityId,
        bool exists,
        string? expectedTemplate,
        long? expectedRevision,
        bool? expectedTombstone,
        Vector3State? expectedPosition,
        bool requireStaleEvidence,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureEntitiesAsync(node, [entityId], cancellationToken)
                .ConfigureAwait(false);
            EntitySnapshotV2 entity = FindEntity(last, entityId);
            bool matches = entity.Exists == exists
                && (expectedTemplate == null || string.Equals(entity.TemplateName, expectedTemplate, StringComparison.Ordinal))
                && (!expectedRevision.HasValue || entity.Revision == expectedRevision.Value)
                && (!expectedTombstone.HasValue || entity.IsTombstone == expectedTombstone.Value)
                && (expectedPosition == null
                    || entity.Body != null && Distance(entity.Body.Position, expectedPosition) <= 0.35)
                && (!requireStaleEvidence
                    || last.EntityLifecycle?.IgnoredStalePackets > 0);
            if (matches) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge entity {entityId}. ExpectedExists={exists}; ExpectedRevision={expectedRevision}; Last={last?.Entities?.SingleOrDefault()}; Lifecycle={last?.EntityLifecycle}.");
    }

    private static async Task<StateSnapshotV2> WaitForEntitySnapshotAsync(
        GameNodeProcess node,
        StateSnapshotV2 expected,
        int entityId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        SnapshotV2Diff? lastDifference = null;
        int consecutiveMatches = 0;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureEntitiesAsync(node, [entityId], cancellationToken)
                .ConfigureAwait(false);
            lastDifference = SnapshotV2Comparer.CompareEntities(expected, last);
            if (lastDifference.Equal) {
                consecutiveMatches++;
                if (consecutiveMatches >= 2) {
                    return last;
                }
            }
            else {
                consecutiveMatches = 0;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not settle entity {entityId} to the authoritative body snapshot. "
            + $"Differences={string.Join(", ", lastDifference?.Differences.Select(item => item.Path) ?? [])}; "
            + $"Last={last?.Entities?.SingleOrDefault()}.");
    }

    private static async Task<StateSnapshotV2> WaitForCurrentEntitiesAsync(
        GameNodeProcess node,
        EntityLifecycleFixture fixture,
        EntityLifecycleBurstResult burst,
        bool requireHistoricalTombstone,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        _ = await WaitForEntityAsync(
            node,
            fixture.EntityId,
            true,
            fixture.TemplateName,
            fixture.Revision,
            false,
            expectedPosition: null,
            requireStaleEvidence: false,
            timeout,
            cancellationToken).ConfigureAwait(false);
        _ = await WaitForEntityAsync(
            node,
            burst.Replacement.EntityId,
            true,
            burst.Replacement.TemplateName,
            burst.FinalReplacementRevision,
            false,
            expectedPosition: null,
            requireStaleEvidence: false,
            timeout,
            cancellationToken).ConfigureAwait(false);
        _ = await WaitForEntityAsync(
            node,
            burst.TombstoneEntityId,
            false,
            expectedTemplate: null,
            expectedRevision: requireHistoricalTombstone ? burst.FinalTombstoneRevision : null,
            expectedTombstone: requireHistoricalTombstone ? true : null,
            expectedPosition: null,
            requireStaleEvidence: false,
            timeout,
            cancellationToken).ConfigureAwait(false);
        return await CaptureEntitiesAsync(
            node,
            [fixture.EntityId, burst.Replacement.EntityId, burst.TombstoneEntityId],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StateSnapshotV2> WaitForPersistedEntitiesAsync(
        GameNodeProcess node,
        EntityLifecycleFixture fixture,
        EntityLifecycleBurstResult burst,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        int[] ids = [fixture.EntityId, burst.Replacement.EntityId, burst.TombstoneEntityId];
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureEntitiesAsync(node, ids, cancellationToken).ConfigureAwait(false);
            EntitySnapshotV2 survivor = FindEntity(last, fixture.EntityId);
            EntitySnapshotV2 replacement = FindEntity(last, burst.Replacement.EntityId);
            EntitySnapshotV2 removed = FindEntity(last, burst.TombstoneEntityId);
            if (survivor.Exists
                && replacement.Exists
                && !removed.Exists
                && string.Equals(survivor.TemplateName, fixture.TemplateName, StringComparison.Ordinal)
                && string.Equals(replacement.TemplateName, burst.Replacement.TemplateName, StringComparison.Ordinal)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose persisted entity lifecycle state. Last={FormatEntities(last)}.");
    }

    private static Task<StateSnapshotV2> CaptureEntitiesAsync(
        GameNodeProcess node,
        IEnumerable<int> entityIds,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Entities],
                EntityIds: entityIds.Distinct().ToArray()),
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
            ActionEventRecord? rejected = selected.FirstOrDefault(item => item.Phase == ActionEventPhase.Rejected);
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
                    throw new InvalidOperationException("Server declined to save the entity lifecycle world.");
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
                    "Saved entity lifecycle world did not become playable.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return savedWorldDirectory;
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        IReadOnlyCollection<int> entityIds,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            if (entityIds.Count > 0) {
                try {
                    snapshots[$"{node.Role}-final"] = await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                        ControlCommandNames.GetSnapshotV2,
                        new GetSnapshotV2Command(
                            [SnapshotDomain.Network, SnapshotDomain.Entities, SnapshotDomain.Events],
                            EventLimit: AutomationProtocol.MaximumEventQueryCount,
                            EntityIds: entityIds.Distinct().ToArray()),
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

    private static void EnsureFixture(EntityLifecycleFixture fixture) {
        if (fixture.EntityId <= 0
            || fixture.Revision <= 0
            || !string.Equals(fixture.Alias, "survivor", StringComparison.Ordinal)
            || !string.Equals(fixture.TemplateName, "Cow", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Entity lifecycle fixture is incomplete or inconsistent.");
        }
    }

    private static void ValidateBurstResult(EntityLifecycleBurstResult burst) {
        if (burst.Replacement.EntityId <= 0
            || burst.TombstoneEntityId <= 0
            || burst.Replacement.EntityId == burst.TombstoneEntityId
            || burst.StaleReplacementRevision >= burst.FinalReplacementRevision
            || burst.StaleTombstoneRevision >= burst.FinalTombstoneRevision) {
            throw new InvalidOperationException($"Entity lifecycle burst returned invalid revisions: {burst}.");
        }
    }

    private static void EnsureEntitySnapshotsEqual(IReadOnlyList<StateSnapshotV2> values) {
        StateSnapshotV2 expected = values[0];
        foreach (StateSnapshotV2 actual in values.Skip(1)) {
            SnapshotV2Diff diff = SnapshotV2Comparer.CompareEntities(expected, actual);
            if (!diff.Equal) {
                throw new InvalidOperationException(
                    "Entity snapshots diverged: "
                    + string.Join(", ", diff.Differences.Select(item => item.Path)));
            }
        }
    }

    private static void EnsureEntityExistenceEqual(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        IEnumerable<int> entityIds) {
        foreach (int entityId in entityIds) {
            EntitySnapshotV2 expectedEntity = FindEntity(expected, entityId);
            EntitySnapshotV2 actualEntity = FindEntity(actual, entityId);
            if (expectedEntity.Exists != actualEntity.Exists
                || !string.Equals(expectedEntity.TemplateName, actualEntity.TemplateName, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    $"Entity persistence mismatch for {entityId}. Server={expectedEntity}; Client={actualEntity}.");
            }
        }
    }

    private static EntitySnapshotV2 FindEntity(StateSnapshotV2 snapshot, int entityId) =>
        snapshot.Entities?.Single(entity => entity.EntityId == entityId)
        ?? throw new InvalidOperationException($"Snapshot omitted selected entity {entityId}.");

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

    private static void ValidateReplayPlan(EntityCreatureReplayPlan? replayPlan) {
        if (replayPlan == null) {
            return;
        }
        if (!string.Equals(replayPlan.Impulse.Alias, "survivor", StringComparison.Ordinal)
            || replayPlan.Impulse.Impulse == null
            || replayPlan.Impulse.ActiveFrames is < 2 or > 120
            || !string.Equals(replayPlan.LifecycleBurst.ReplacementAlias, "replacement", StringComparison.Ordinal)
            || !string.Equals(replayPlan.LifecycleBurst.TombstoneAlias, "tombstone", StringComparison.Ordinal)
            || !replayPlan.LifecycleBurst.SendStaleLoads) {
            throw new InvalidDataException("Entity creature replay actions are invalid.");
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

    private static double Length(Vector3State value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static string FormatVector(Vector3State value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value.X:R},{value.Y:R},{value.Z:R}");

    private static string FormatEntities(StateSnapshotV2? snapshot) =>
        snapshot?.Entities == null
            ? "<none>"
            : string.Join(" | ", snapshot.Entities.Select(entity => entity.ToString()));

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
