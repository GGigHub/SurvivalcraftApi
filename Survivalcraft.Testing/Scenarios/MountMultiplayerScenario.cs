using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record MountReplayPlan(
    PlayerMountCommand Mount,
    PlayerMountCommand Dismount,
    PlayerMountCommand FaultMount,
    RunMountRevisionBurstCommand RevisionBurst,
    RunMountLifecycleBurstCommand LifecycleBurst,
    PlayerMountCommand Remount,
    PlayerMountCommand ReconnectMount);

public sealed record MountMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    MountReplayPlan? ReplayPlan = null);

public sealed record MountMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class MountMultiplayerScenarioException : Exception {
    public MountMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class MountMultiplayerScenario {
    public const string ScenarioName = "mount-multiplayer";
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
        new("verify-control-authority", "typed authority rejection"),
        new("prepare-mount-fixtures", ControlCommandNames.PrepareMountFixture),
        new("verify-security-probes", ControlCommandNames.PlayerMount),
        new("mount", ControlCommandNames.PlayerMount),
        new("dismount", ControlCommandNames.PlayerMount),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("mount-under-faults", ControlCommandNames.PlayerMount),
        new("run-mount-revision-burst", ControlCommandNames.RunMountRevisionBurst),
        new("run-mount-lifecycle-burst", ControlCommandNames.RunMountLifecycleBurst),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("connect-late-client", "network.connect(client-c)"),
        new("verify-late-join", ControlCommandNames.GetSnapshotV2),
        new("remount-before-disconnect", ControlCommandNames.PlayerMount),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("verify-mount-freed", ControlCommandNames.GetSnapshotV2),
        new("reconnect-actor", "network.connect(client-a)"),
        new("verify-reconnect", ControlCommandNames.GetSnapshotV2),
        new("mount-before-persistence", ControlCommandNames.PlayerMount),
        new("save-and-stop-world", ControlCommandNames.SaveWorld),
        new("reload-saved-world", ControlCommandNames.ReloadWorld),
        new("restart-transport-host", "network.startHost"),
        new("connect-persistence-clients", "network.connect(client-a, client-c)"),
        new("verify-persistence-boundary", ControlCommandNames.GetSnapshotV2)
    ];

    public static NetworkFaultProfile CreateDefaultNetworkFaults(int seed) =>
        new(
            Seed: unchecked(seed + 811),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18);

    public static MountReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        (string Role, string Command)[] expected = [
            ("client-a", ControlCommandNames.PlayerMount),
            ("client-a", ControlCommandNames.PlayerMount),
            ("client-a", ControlCommandNames.PlayerMount),
            ("server", ControlCommandNames.RunMountRevisionBurst),
            ("server", ControlCommandNames.RunMountLifecycleBurst),
            ("client-a", ControlCommandNames.PlayerMount),
            ("client-a", ControlCommandNames.PlayerMount)
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
        MountReplayPlan plan = new(
            DeserializeAction<PlayerMountCommand>(records[0]),
            DeserializeAction<PlayerMountCommand>(records[1]),
            DeserializeAction<PlayerMountCommand>(records[2]),
            DeserializeAction<RunMountRevisionBurstCommand>(records[3]),
            DeserializeAction<RunMountLifecycleBurstCommand>(records[4]),
            DeserializeAction<PlayerMountCommand>(records[5]),
            DeserializeAction<PlayerMountCommand>(records[6]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<MountMultiplayerScenarioResult> RunAsync(
        MountMultiplayerScenarioOptions options,
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
                "Mount resilience requires a non-empty network fault profile.",
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
        HashSet<int> observedEntityIds = [];
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
                    ControlCommandNames.PrepareMountFixture,
                    ControlCommandNames.PlayerMount,
                    ControlCommandNames.RunMountRevisionBurst,
                    ControlCommandNames.RunMountLifecycleBurst
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Mechanics
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"Mount-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            int stableActorPlayerIndex = actorPlayerIndex;
            NodeStatus observerStatus = await cluster.Clients[1].GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);

            await VerifyControlAuthorityAsync(cluster, runner, actorPlayerIndex, scenarioToken)
                .ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-control-authority-enforcement",
                "passed",
                [cluster.Server.Role, cluster.Clients[0].Role, cluster.Clients[1].Role],
                "server cannot issue client mount actions and clients cannot create fixtures or run authoritative bursts"));
            await runner.RunAsync(
                "reset-events",
                ControlCommandNames.ResetEvents,
                token => Task.WhenAll(MultiplayerGameplayBootstrap.AllNodes(cluster).Select(node =>
                    node.SendAsync<EmptyCommand, ResetEventsResult>(
                        ControlCommandNames.ResetEvents,
                        new EmptyCommand(),
                        cancellationToken: token))),
                scenarioToken).ConfigureAwait(false);

            MountFixture primary = await runner.RunAsync(
                "prepare-primary-mount",
                ControlCommandNames.PrepareMountFixture,
                token => cluster.Server.SendAsync<PrepareMountFixtureCommand, MountFixture>(
                    ControlCommandNames.PrepareMountFixture,
                    new PrepareMountFixtureCommand(actorPlayerIndex),
                    cancellationToken: token),
                value => $"rider={value.RiderEntityId}; mount={value.MountEntityId}@{value.MountLifecycleRevision}",
                scenarioToken).ConfigureAwait(false);
            MountFixture far = await runner.RunAsync(
                "prepare-out-of-range-mount",
                ControlCommandNames.PrepareMountFixture,
                token => cluster.Server.SendAsync<PrepareMountFixtureCommand, MountFixture>(
                    ControlCommandNames.PrepareMountFixture,
                    new PrepareMountFixtureCommand(actorPlayerIndex, "far", Distance: 12),
                    cancellationToken: token),
                value => $"mount={value.MountEntityId}; distance=12",
                scenarioToken).ConfigureAwait(false);
            MountFixture occupied = await runner.RunAsync(
                "prepare-occupied-mount",
                ControlCommandNames.PrepareMountFixture,
                token => cluster.Server.SendAsync<PrepareMountFixtureCommand, MountFixture>(
                    ControlCommandNames.PrepareMountFixture,
                    new PrepareMountFixtureCommand(observerStatus.PlayerIndex, "occupied"),
                    cancellationToken: token),
                value => $"rider={value.RiderEntityId}; mount={value.MountEntityId}",
                scenarioToken).ConfigureAwait(false);
            ValidateFixtures(primary, far, actorPlayerIndex);
            if (occupied.PlayerIndex != observerStatus.PlayerIndex
                || occupied.RiderEntityId <= 0
                || occupied.MountEntityId <= 0
                || occupied.MountLifecycleRevision <= 0) {
                throw new InvalidOperationException(
                    $"Occupied-target fixture is incomplete: {occupied}.");
            }
            observedEntityIds.UnionWith([
                primary.RiderEntityId,
                primary.MountEntityId,
                far.MountEntityId,
                occupied.RiderEntityId,
                occupied.MountEntityId
            ]);
            GameNodeProcess[] initialNodes = [
                cluster.Server,
                cluster.Clients[0],
                cluster.Clients[1]
            ];
            StateSnapshotV2[] initialSnapshots = await WaitForRelationOnNodesAsync(
                initialNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: primary.MountRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, "initial-unmounted", initialSnapshots, initialNodes);
            _ = await WaitForRelationOnNodesAsync(
                initialNodes,
                occupied.RiderEntityId,
                occupied.MountEntityId,
                mounted: false,
                expectedRevision: occupied.MountRevision,
                expectedMountLifecycle: occupied.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            int observerRiderEntityId = await WaitForPlayerRiderEntityIdAsync(
                cluster.Server,
                observerStatus.PlayerIndex,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            await runner.RunAsync(
                "verify-security-probes",
                ControlCommandNames.PlayerMount,
                async token => {
                    _ = await cluster.Clients[0].SendAsync<PlayerMountCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMount,
                        new PlayerMountCommand(
                            actorPlayerIndex,
                            primary.MountEntityId,
                            Mount: true,
                            TargetRiderEntityId: observerRiderEntityId,
                            ExpectRejected: true),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await cluster.Clients[0].SendAsync<PlayerMountCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMount,
                        new PlayerMountCommand(
                            actorPlayerIndex,
                            primary.MountEntityId,
                            Mount: true,
                            ForgeAuthoritativeState: true,
                            ExpectRejected: true),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await cluster.Clients[0].SendAsync<PlayerMountCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMount,
                        new PlayerMountCommand(
                            actorPlayerIndex,
                            far.MountEntityId,
                            Mount: true,
                            ExpectRejected: true),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await WaitForRelationOnNodesAsync(
                        initialNodes,
                        primary.RiderEntityId,
                        primary.MountEntityId,
                        mounted: false,
                        expectedRevision: primary.MountRevision,
                        expectedMountLifecycle: primary.MountLifecycleRevision,
                        stepTimeout,
                        token).ConfigureAwait(false);

                    _ = await cluster.Clients[1].SendAsync<PlayerMountCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMount,
                        new PlayerMountCommand(
                            observerStatus.PlayerIndex,
                            occupied.MountEntityId,
                            Mount: true),
                        cancellationToken: token).ConfigureAwait(false);
                    long occupiedMountedRevision = checked(occupied.MountRevision + 1);
                    _ = await WaitForRelationOnNodesAsync(
                        initialNodes,
                        occupied.RiderEntityId,
                        occupied.MountEntityId,
                        mounted: true,
                        expectedRevision: occupiedMountedRevision,
                        expectedMountLifecycle: occupied.MountLifecycleRevision,
                        stepTimeout,
                        token).ConfigureAwait(false);

                    _ = await cluster.Clients[0].SendAsync<PlayerMountCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMount,
                        new PlayerMountCommand(
                            actorPlayerIndex,
                            occupied.MountEntityId,
                            Mount: true,
                            ExpectRejected: true),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await WaitForRelationOnNodesAsync(
                        initialNodes,
                        primary.RiderEntityId,
                        primary.MountEntityId,
                        mounted: false,
                        expectedRevision: primary.MountRevision,
                        expectedMountLifecycle: primary.MountLifecycleRevision,
                        stepTimeout,
                        token).ConfigureAwait(false);
                    _ = await WaitForRelationOnNodesAsync(
                        initialNodes,
                        occupied.RiderEntityId,
                        occupied.MountEntityId,
                        mounted: true,
                        expectedRevision: occupiedMountedRevision,
                        expectedMountLifecycle: occupied.MountLifecycleRevision,
                        stepTimeout,
                        token).ConfigureAwait(false);

                    _ = await cluster.Clients[1].SendAsync<PlayerMountCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMount,
                        new PlayerMountCommand(
                            observerStatus.PlayerIndex,
                            occupied.MountEntityId,
                            Mount: false),
                        cancellationToken: token).ConfigureAwait(false);
                    _ = await WaitForRelationOnNodesAsync(
                        initialNodes,
                        occupied.RiderEntityId,
                        occupied.MountEntityId,
                        mounted: false,
                        expectedRevision: checked(occupiedMountedRevision + 1),
                        expectedMountLifecycle: occupied.MountLifecycleRevision,
                        stepTimeout,
                        token).ConfigureAwait(false);
                },
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2[] securitySnapshots = await WaitForRelationOnNodesAsync(
                initialNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: primary.MountRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, "after-security-probes", securitySnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-session-lifecycle-and-range-authority",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "cross-player rider impersonation, forged authoritative state, out-of-range request, and occupied-target takeover left authoritative relationships unchanged"));

            PlayerMountCommand mountCommand = options.ReplayPlan?.Mount
                ?? new PlayerMountCommand(actorPlayerIndex, primary.MountEntityId, Mount: true);
            ActionReceipt mountReceipt = await SendMountActionAsync(
                runner,
                cluster.Clients[0],
                "mount",
                mountCommand,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.PlayerMount,
                mountCommand, mountReceipt));
            events["client-a-mount"] = await WaitForAppliedEventAsync(
                cluster.Clients[0], mountReceipt.ActionId, stepTimeout, scenarioToken)
                .ConfigureAwait(false);
            long mountRevision = checked(primary.MountRevision + 1);
            StateSnapshotV2[] mountedSnapshots = await WaitForRelationOnNodesAsync(
                initialNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: true,
                expectedRevision: mountRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, "mounted", mountedSnapshots, initialNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-authority-observer-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"rider={primary.RiderEntityId}; mount={primary.MountEntityId}; revision={mountRevision}"));

            PlayerMountCommand dismountCommand = options.ReplayPlan?.Dismount
                ?? new PlayerMountCommand(actorPlayerIndex, primary.MountEntityId, Mount: false);
            ActionReceipt dismountReceipt = await SendMountActionAsync(
                runner,
                cluster.Clients[0],
                "dismount",
                dismountCommand,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.PlayerMount,
                dismountCommand, dismountReceipt));
            events["client-a-dismount"] = await WaitForAppliedEventAsync(
                cluster.Clients[0], dismountReceipt.ActionId, stepTimeout, scenarioToken)
                .ConfigureAwait(false);
            long dismountRevision = checked(mountRevision + 1);
            StateSnapshotV2[] dismountedSnapshots = await WaitForRelationOnNodesAsync(
                initialNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: dismountRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, "dismounted", dismountedSnapshots, initialNodes);

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);
            PlayerMountCommand faultMountCommand = options.ReplayPlan?.FaultMount
                ?? new PlayerMountCommand(actorPlayerIndex, primary.MountEntityId, Mount: true);
            ActionReceipt faultMountReceipt = await SendMountActionAsync(
                runner,
                cluster.Clients[0],
                "mount-under-faults",
                faultMountCommand,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.PlayerMount,
                faultMountCommand, faultMountReceipt));
            long faultMountRevision = checked(dismountRevision + 1);
            _ = await WaitForRelationOnNodesAsync(
                initialNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: true,
                expectedRevision: faultMountRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            RunMountRevisionBurstCommand revisionBurst = options.ReplayPlan?.RevisionBurst
                ?? new RunMountRevisionBurstCommand(
                    primary.RiderEntityId,
                    primary.MountEntityId,
                    FinalMounted: false,
                    SendStaleState: true);
            MountRevisionBurstResult revisionResult = await runner.RunAsync(
                "run-mount-revision-burst",
                ControlCommandNames.RunMountRevisionBurst,
                token => cluster.Server.SendAsync<RunMountRevisionBurstCommand, MountRevisionBurstResult>(
                    ControlCommandNames.RunMountRevisionBurst,
                    revisionBurst,
                    cancellationToken: token),
                value => $"stale={value.StaleRevision}; final={value.FinalRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(revisionResult.Receipt);
            if (revisionResult.FinalMounted
                || revisionResult.StaleRevision >= revisionResult.FinalRevision) {
                throw new InvalidOperationException($"Invalid mount revision burst: {revisionResult}.");
            }
            actions.Add(ActionRecord(cluster.Server.Role, ControlCommandNames.RunMountRevisionBurst,
                revisionBurst, revisionResult.Receipt));
            events["server-mount-revision-burst"] = await WaitForAppliedEventAsync(
                cluster.Server, revisionResult.Receipt.ActionId, stepTimeout, scenarioToken)
                .ConfigureAwait(false);
            StateSnapshotV2[] revisionSnapshots = await WaitForRelationOnNodesAsync(
                initialNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: revisionResult.FinalRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken,
                requireIgnoredStaleOnClients: true).ConfigureAwait(false);
            AddSnapshots(snapshots, "after-revision-burst", revisionSnapshots, initialNodes);

            RunMountLifecycleBurstCommand lifecycleBurst = options.ReplayPlan?.LifecycleBurst
                ?? new RunMountLifecycleBurstCommand(
                    primary.Alias,
                    primary.RiderEntityId,
                    ReuseEntityId: true,
                    SendStaleState: true);
            MountLifecycleBurstResult lifecycleResult = await runner.RunAsync(
                "run-mount-lifecycle-burst",
                ControlCommandNames.RunMountLifecycleBurst,
                token => cluster.Server.SendAsync<RunMountLifecycleBurstCommand, MountLifecycleBurstResult>(
                    ControlCommandNames.RunMountLifecycleBurst,
                    lifecycleBurst,
                    cancellationToken: token),
                value => $"mount={value.Replacement.MountEntityId}; lifecycle={value.StaleMountLifecycleRevision}->{value.FinalMountLifecycleRevision}; injectedStaleRevision={value.InjectedStaleRevision}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(lifecycleResult.Receipt);
            ValidateLifecycleResult(primary, lifecycleResult);
            primary = lifecycleResult.Replacement;
            observedEntityIds.Add(primary.MountEntityId);
            actions.Add(ActionRecord(cluster.Server.Role, ControlCommandNames.RunMountLifecycleBurst,
                lifecycleBurst, lifecycleResult.Receipt));
            events["server-mount-lifecycle-burst"] = await WaitForAppliedEventAsync(
                cluster.Server, lifecycleResult.Receipt.ActionId, stepTimeout, scenarioToken)
                .ConfigureAwait(false);
            StateSnapshotV2[] lifecycleSnapshots = await WaitForRelationOnNodesAsync(
                initialNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: lifecycleResult.AuthoritativeMountRevision,
                expectedMountLifecycle: lifecycleResult.FinalMountLifecycleRevision,
                stepTimeout,
                scenarioToken,
                requireLifecycleRejectedOnClients: true).ConfigureAwait(false);
            AddSnapshots(snapshots, "after-lifecycle-burst", lifecycleSnapshots, initialNodes);
            if (lifecycleSnapshots.Skip(1).Any(snapshot =>
                    snapshot.StateRevisions?.IgnoredLifecycleStates <= 0)) {
                throw new InvalidOperationException(
                    "A client did not report rejecting the delayed state from the removed mount generation.");
            }
            long duplicatedPackets = 0;
            foreach (GameNodeProcess node in initialNodes) {
                NodeStatus status = await node.GetStatusAsync(scenarioToken).ConfigureAwait(false);
                duplicatedPackets += status.NetworkFaults.Duplicated;
            }
            if (duplicatedPackets <= 0) {
                throw new InvalidOperationException("Fault injection did not duplicate mount packets.");
            }
            metrics["fault.duplicatedPackets"] = duplicatedPackets.ToString(CultureInfo.InvariantCulture);
            metrics["fault.profile"] = FormatFaultProfile(faultProfile);
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-stale-duplicate-reorder-lifecycle-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"authoritativeRevision={lifecycleResult.AuthoritativeMountRevision}; rejectedInjectedStaleRevision={lifecycleResult.InjectedStaleRevision}; mountLifecycle={lifecycleResult.FinalMountLifecycleRevision}; duplicated={duplicatedPackets}"));

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
            StateSnapshotV2 lateSnapshot = await WaitForRelationAsync(
                lateClient,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: lifecycleResult.AuthoritativeMountRevision,
                expectedMountLifecycle: lifecycleResult.FinalMountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["client-c-late-join"] = lateSnapshot;
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-late-join-baseline",
                "passed",
                [lateClient.Role],
                $"current replacement mount generation {primary.MountEntityId}@{primary.MountLifecycleRevision} arrived unmounted"));

            GameNodeProcess[] allPlayingNodes = [
                cluster.Server,
                cluster.Clients[0],
                cluster.Clients[1],
                lateClient
            ];
            PlayerMountCommand remountCommand = options.ReplayPlan?.Remount
                ?? new PlayerMountCommand(actorPlayerIndex, primary.MountEntityId, Mount: true);
            ActionReceipt remountReceipt = await SendMountActionAsync(
                runner,
                cluster.Clients[0],
                "remount-before-disconnect",
                remountCommand,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(cluster.Clients[0].Role, ControlCommandNames.PlayerMount,
                remountCommand, remountReceipt));
            long remountRevision = checked(lifecycleResult.AuthoritativeMountRevision + 1);
            _ = await WaitForRelationOnNodesAsync(
                allPlayingNodes,
                primary.RiderEntityId,
                primary.MountEntityId,
                mounted: true,
                expectedRevision: remountRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            GameNodeProcess actorClient = cluster.Clients[0];
            long oldRiderLifecycle = primary.RiderLifecycleRevision;
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
            GameNodeProcess[] remainingNodes = [cluster.Server, cluster.Clients[1], lateClient];
            StateSnapshotV2[] freedSnapshots = await Task.WhenAll(remainingNodes.Select(node =>
                WaitForRiderAbsentAndMountFreeAsync(
                    node,
                    primary.RiderEntityId,
                    primary.MountEntityId,
                    stepTimeout,
                    scenarioToken))).ConfigureAwait(false);
            AddSnapshots(snapshots, "actor-disconnected", freedSnapshots, remainingNodes);
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-rider-removal-frees-target",
                "passed",
                remainingNodes.Select(node => node.Role).ToArray(),
                $"removed rider={primary.RiderEntityId}; mount={primary.MountEntityId} has no authoritative rider"));

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
            if (reconnectStatus.PlayerIndex != stableActorPlayerIndex) {
                throw new InvalidOperationException(
                    $"Actor identity changed from {stableActorPlayerIndex} to {reconnectStatus.PlayerIndex}.");
            }
            actorPlayerIndex = reconnectStatus.PlayerIndex;
            int reconnectRiderEntityId = await WaitForPlayerRiderEntityIdAsync(
                cluster.Server,
                actorPlayerIndex,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 reconnectServer = await WaitForRelationAsync(
                cluster.Server,
                reconnectRiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: null,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            MechanicsStateSnapshotV2 reconnectRider = FindMechanics(
                reconnectServer,
                reconnectRiderEntityId);
            if (reconnectRiderEntityId == primary.RiderEntityId
                && reconnectRider.LifecycleRevision <= oldRiderLifecycle) {
                throw new InvalidOperationException(
                    "Reconnected actor reused the old rider identity without advancing lifecycle generation.");
            }
            long reconnectMountRevision = reconnectRider.MountRevision;
            GameNodeProcess[] reconnectedNodes = [
                cluster.Server,
                actorClient,
                cluster.Clients[1],
                lateClient
            ];
            StateSnapshotV2[] reconnectSnapshots = await WaitForRelationOnNodesAsync(
                reconnectedNodes,
                reconnectRiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: reconnectMountRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, "actor-reconnected", reconnectSnapshots, reconnectedNodes);
            observedEntityIds.Add(reconnectRiderEntityId);
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-reconnect-safe-dismount-generation",
                "passed",
                reconnectedNodes.Select(node => node.Role).ToArray(),
                $"stablePlayerIndex={actorPlayerIndex}; rider={primary.RiderEntityId}@{oldRiderLifecycle}->{reconnectRiderEntityId}@{reconnectRider.LifecycleRevision}; mount free"));

            PlayerMountCommand reconnectMountCommand = options.ReplayPlan?.ReconnectMount
                ?? new PlayerMountCommand(actorPlayerIndex, primary.MountEntityId, Mount: true);
            ActionReceipt reconnectMountReceipt = await SendMountActionAsync(
                runner,
                actorClient,
                "mount-before-persistence",
                reconnectMountCommand,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.PlayerMount,
                reconnectMountCommand, reconnectMountReceipt));
            long prePersistenceRevision = checked(reconnectMountRevision + 1);
            StateSnapshotV2[] prePersistenceSnapshots = await WaitForRelationOnNodesAsync(
                reconnectedNodes,
                reconnectRiderEntityId,
                primary.MountEntityId,
                mounted: true,
                expectedRevision: prePersistenceRevision,
                expectedMountLifecycle: primary.MountLifecycleRevision,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, "before-persistence", prePersistenceSnapshots, reconnectedNodes);

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
                "connect-persistence-clients",
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
                    return await actorClient.GetStatusAsync(token).ConfigureAwait(false);
                },
                status => $"actorPlayerIndex={status.PlayerIndex}",
                scenarioToken).ConfigureAwait(false);
            NodeStatus persistedActorStatus = await actorClient.GetStatusAsync(scenarioToken)
                .ConfigureAwait(false);
            if (persistedActorStatus.PlayerIndex != stableActorPlayerIndex) {
                throw new InvalidOperationException(
                    $"Actor persistence identity changed from {stableActorPlayerIndex} to {persistedActorStatus.PlayerIndex}.");
            }
            int persistedRiderEntityId = await WaitForPlayerRiderEntityIdAsync(
                cluster.Server,
                stableActorPlayerIndex,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            StateSnapshotV2 persistedServer = await WaitForRelationAsync(
                cluster.Server,
                persistedRiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: null,
                expectedMountLifecycle: null,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            MechanicsStateSnapshotV2 persistedRider = FindMechanics(
                persistedServer,
                persistedRiderEntityId);
            long persistedRevision = persistedRider.MountRevision;
            long persistedMountLifecycle = FindMechanics(
                persistedServer,
                primary.MountEntityId).LifecycleRevision;
            GameNodeProcess[] persistenceNodes = [cluster.Server, actorClient, lateClient];
            StateSnapshotV2[] persistenceSnapshots = await WaitForRelationOnNodesAsync(
                persistenceNodes,
                persistedRiderEntityId,
                primary.MountEntityId,
                mounted: false,
                expectedRevision: persistedRevision,
                expectedMountLifecycle: persistedMountLifecycle,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, "after-persistence", persistenceSnapshots, persistenceNodes);
            EntitySnapshotV2 persistedMount = persistedServer.Entities?
                .Single(entity => entity.EntityId == primary.MountEntityId)
                ?? throw new InvalidOperationException("Persistence snapshot omitted the mount entity.");
            if (!persistedMount.Exists
                || !string.Equals(persistedMount.TemplateName, primary.TemplateName, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    $"Persistent mount entity is missing or changed: {persistedMount}.");
            }
            assertions.Add(new ScenarioAssertionRecordV2(
                "mount-save-reload-safe-dismount-persistence",
                "passed",
                persistenceNodes.Select(node => node.Role).ToArray(),
                $"world={savedWorldDirectory}; mount={primary.MountEntityId} persisted; transient rider-parent relation reset before reconnect"));
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
            throw new MountMultiplayerScenarioException(
                $"Mount multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new MountMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new MountMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private static async Task VerifyControlAuthorityAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int actorPlayerIndex,
        CancellationToken cancellationToken) {
        await runner.RunAsync(
            "verify-control-authority",
            "typed authority rejection",
            async token => {
                await RequireRejectionAsync(
                    () => cluster.Clients[0].SendAsync<PrepareMountFixtureCommand, MountFixture>(
                        ControlCommandNames.PrepareMountFixture,
                        new PrepareMountFixtureCommand(actorPlayerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<PlayerMountCommand, ActionReceipt>(
                        ControlCommandNames.PlayerMount,
                        new PlayerMountCommand(actorPlayerIndex, 1, true),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunMountRevisionBurstCommand, MountRevisionBurstResult>(
                        ControlCommandNames.RunMountRevisionBurst,
                        new RunMountRevisionBurstCommand(1, 2),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunMountLifecycleBurstCommand, MountLifecycleBurstResult>(
                        ControlCommandNames.RunMountLifecycleBurst,
                        new RunMountLifecycleBurstCommand("primary", 1),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ActionReceipt> SendMountActionAsync(
        ScenarioRunner runner,
        GameNodeProcess client,
        string stepName,
        PlayerMountCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        ActionReceipt receipt = await runner.RunAsync(
            stepName,
            ControlCommandNames.PlayerMount,
            token => client.SendAsync<PlayerMountCommand, ActionReceipt>(
                ControlCommandNames.PlayerMount,
                command,
                timeout: timeout,
                cancellationToken: token),
            value => $"actionId={value.ActionId}; code={value.Code}",
            cancellationToken).ConfigureAwait(false);
        EnsureAccepted(receipt);
        return receipt;
    }

    private static async Task<StateSnapshotV2[]> WaitForRelationOnNodesAsync(
        IReadOnlyList<GameNodeProcess> nodes,
        int riderEntityId,
        int mountEntityId,
        bool mounted,
        long? expectedRevision,
        long? expectedMountLifecycle,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool requireIgnoredStaleOnClients = false,
        bool requireLifecycleRejectedOnClients = false) =>
        await Task.WhenAll(nodes.Select(node => WaitForRelationAsync(
            node,
            riderEntityId,
            mountEntityId,
            mounted,
            expectedRevision,
            expectedMountLifecycle,
            timeout,
            cancellationToken,
            requireIgnoredStaleOnClients
                && !string.Equals(node.Role, "server", StringComparison.Ordinal),
            requireLifecycleRejectedOnClients
                && !string.Equals(node.Role, "server", StringComparison.Ordinal))))
            .ConfigureAwait(false);

    private static async Task<StateSnapshotV2> WaitForRelationAsync(
        GameNodeProcess node,
        int riderEntityId,
        int mountEntityId,
        bool mounted,
        long? expectedRevision,
        long? expectedMountLifecycle,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool requireIgnoredStale = false,
        bool requireLifecycleRejected = false) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        int consecutiveMatches = 0;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureMechanicsAsync(
                node,
                [riderEntityId, mountEntityId],
                cancellationToken).ConfigureAwait(false);
            MechanicsStateSnapshotV2 rider = FindMechanics(last, riderEntityId);
            MechanicsStateSnapshotV2 mount = FindMechanics(last, mountEntityId);
            bool relationMatches = mounted
                ? rider.MountEntityId == mountEntityId && mount.RiderEntityId == riderEntityId
                : rider.MountEntityId == null && mount.RiderEntityId == null;
            bool matches = rider.Exists
                && rider.HasRider
                && mount.Exists
                && mount.HasMount
                && relationMatches
                && !rider.MountAnimating
                && !rider.IsDismounting
                && (!expectedRevision.HasValue || rider.MountRevision == expectedRevision.Value)
                && (!expectedMountLifecycle.HasValue
                    || mount.LifecycleRevision == expectedMountLifecycle.Value)
                && (!mounted
                    || rider.MountEntityLifecycleRevision == mount.LifecycleRevision)
                && (!requireIgnoredStale
                    || last.StateRevisions?.IgnoredStaleStates > 0)
                && (!requireLifecycleRejected
                    || last.StateRevisions?.IgnoredLifecycleStates > 0);
            if (matches) {
                consecutiveMatches++;
                if (consecutiveMatches >= 2) {
                    return last;
                }
            }
            else {
                consecutiveMatches = 0;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge rider {riderEntityId} and mount {mountEntityId}. "
            + $"Mounted={mounted}; Revision={expectedRevision}; MountLifecycle={expectedMountLifecycle}; "
            + $"Last={FormatMechanics(last)}; StateRevisions={last?.StateRevisions}.");
    }

    private static async Task<StateSnapshotV2> WaitForRiderAbsentAndMountFreeAsync(
        GameNodeProcess node,
        int riderEntityId,
        int mountEntityId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        int matches = 0;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureMechanicsAsync(
                node,
                [riderEntityId, mountEntityId],
                cancellationToken).ConfigureAwait(false);
            MechanicsStateSnapshotV2 rider = FindMechanics(last, riderEntityId);
            MechanicsStateSnapshotV2 mount = FindMechanics(last, mountEntityId);
            if (!rider.Exists && mount.Exists && mount.HasMount && mount.RiderEntityId == null) {
                matches++;
                if (matches >= 2) {
                    return last;
                }
            }
            else {
                matches = 0;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' retained removed rider {riderEntityId} or occupied mount {mountEntityId}. Last={FormatMechanics(last)}.");
    }

    private static async Task<int> WaitForPlayerRiderEntityIdAsync(
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
                new GetSnapshotV2Command([SnapshotDomain.Mechanics]),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            MechanicsStateSnapshotV2? rider = last.MechanicsStates?.SingleOrDefault(state =>
                state.Exists
                && state.IsPlayer
                && state.HasRider
                && state.PlayerIndex == playerIndex);
            if (rider != null) {
                return rider.EntityId;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose a rider for player {playerIndex}. Last={FormatMechanics(last)}.");
    }

    private static Task<StateSnapshotV2> CaptureMechanicsAsync(
        GameNodeProcess node,
        int[] entityIds,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Entities, SnapshotDomain.Mechanics],
                EntityIds: entityIds.Distinct().ToArray()),
            cancellationToken: cancellationToken);

    private static MechanicsStateSnapshotV2 FindMechanics(
        StateSnapshotV2 snapshot,
        int entityId) =>
        snapshot.MechanicsStates?.Single(state => state.EntityId == entityId)
        ?? throw new InvalidOperationException(
            $"Snapshot omitted selected mechanics entity {entityId}.");

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
                    throw new InvalidOperationException("Server declined to save the mount world.");
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
                    "Saved mount world did not become playable.",
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
                            [SnapshotDomain.Network, SnapshotDomain.Entities,
                                SnapshotDomain.Mechanics, SnapshotDomain.Events],
                            EventLimit: AutomationProtocol.MaximumEventQueryCount,
                            EntityIds: entityIds.ToArray()),
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

    private static void ValidateFixtures(
        MountFixture primary,
        MountFixture far,
        int actorPlayerIndex) {
        if (primary.PlayerIndex != actorPlayerIndex
            || primary.RiderEntityId <= 0
            || primary.MountEntityId <= 0
            || primary.RiderLifecycleRevision <= 0
            || primary.MountLifecycleRevision <= 0
            || primary.MountRevision < 0
            || !string.Equals(primary.Alias, "primary", StringComparison.Ordinal)
            || !string.Equals(primary.TemplateName, "Boat", StringComparison.Ordinal)
            || far.RiderEntityId != primary.RiderEntityId
            || far.MountEntityId <= 0
            || far.MountEntityId == primary.MountEntityId) {
            throw new InvalidOperationException(
                $"Mount fixtures are incomplete or inconsistent. Primary={primary}; Far={far}.");
        }
    }

    private static void ValidateLifecycleResult(
        MountFixture previous,
        MountLifecycleBurstResult result) {
        if (result.Replacement.MountEntityId != previous.MountEntityId
            || result.Replacement.RiderEntityId != previous.RiderEntityId
            || result.StaleMountLifecycleRevision >= result.FinalMountLifecycleRevision
            || result.AuthoritativeMountRevision < 0
            || result.InjectedStaleRevision >= result.AuthoritativeMountRevision
            || result.Replacement.MountRevision != result.AuthoritativeMountRevision) {
            throw new InvalidOperationException(
                $"Mount lifecycle burst returned invalid generations or revisions: {result}.");
        }
    }

    private static void ValidateReplayPlan(MountReplayPlan? plan) {
        if (plan == null) {
            return;
        }
        PlayerMountCommand[] mountActions = [
            plan.Mount,
            plan.Dismount,
            plan.FaultMount,
            plan.Remount,
            plan.ReconnectMount
        ];
        if (!plan.Mount.Mount
            || plan.Dismount.Mount
            || !plan.FaultMount.Mount
            || !plan.Remount.Mount
            || !plan.ReconnectMount.Mount
            || mountActions.Any(action => action.MountEntityId <= 0
                || action.TargetRiderEntityId.HasValue
                || action.ForgeAuthoritativeState
                || action.ExpectRejected)
            || plan.RevisionBurst.RiderEntityId <= 0
            || plan.RevisionBurst.MountEntityId <= 0
            || plan.RevisionBurst.FinalMounted
            || !plan.RevisionBurst.SendStaleState
            || !string.Equals(plan.LifecycleBurst.Alias, "primary", StringComparison.Ordinal)
            || plan.LifecycleBurst.RiderEntityId <= 0
            || !plan.LifecycleBurst.ReuseEntityId
            || !plan.LifecycleBurst.SendStaleState) {
            throw new InvalidDataException("Mount replay actions are invalid.");
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

    private static string FormatMechanics(StateSnapshotV2? snapshot) =>
        snapshot?.MechanicsStates == null
            ? "<none>"
            : string.Join(" | ", snapshot.MechanicsStates.Select(state => state.ToString()));

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
