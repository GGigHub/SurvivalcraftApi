using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record InventoryHealthReplayPlan(
    InventoryMoveCommand Move,
    InventorySelectSlotCommand SelectSlot,
    InventoryDropCommand Drop,
    RunInventoryRevisionBurstCommand InventoryRevisionBurst,
    PlayerHitCommand Hit,
    RunHealthRevisionBurstCommand HealthRevisionBurst,
    PlayerRequestRespawnCommand Respawn);

public sealed record InventoryHealthMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    NetworkFaultProfile? NetworkFaults = null,
    InventoryHealthReplayPlan? ReplayPlan = null);

public sealed record InventoryHealthMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class InventoryHealthMultiplayerScenarioException : Exception {
    public InventoryHealthMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class InventoryHealthMultiplayerScenario {
    public const string ScenarioName = "inventory-health-multiplayer";
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
        new("prepare-inventory-fixture", ControlCommandNames.PrepareInventoryFixture),
        new("apply-inventory-fixture", ControlCommandNames.ApplyInventoryFixture),
        new("verify-inventory-baseline", ControlCommandNames.GetSnapshotV2),
        new("verify-production-inventory-authority", ControlCommandNames.InventoryMove),
        new("enable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("move-inventory-items", ControlCommandNames.InventoryMove),
        new("select-inventory-slot", ControlCommandNames.InventorySelectSlot),
        new("drop-active-stack", ControlCommandNames.InventoryDrop),
        new("run-inventory-revision-burst", ControlCommandNames.RunInventoryRevisionBurst),
        new("verify-inventory-convergence", ControlCommandNames.GetSnapshotV2),
        new("prepare-health-fixture", ControlCommandNames.PrepareHealthFixture),
        new("apply-health-fixture", ControlCommandNames.ApplyHealthFixture),
        new("verify-production-hit-authority", ControlCommandNames.PlayerHit),
        new("hit-health-target", ControlCommandNames.PlayerHit),
        new("run-health-revision-burst", ControlCommandNames.RunHealthRevisionBurst),
        new("kill-actor", ControlCommandNames.SetHealthFixture),
        new("verify-production-respawn-authority", ControlCommandNames.PlayerRequestRespawn),
        new("request-respawn", ControlCommandNames.PlayerRequestRespawn),
        new("verify-respawn-convergence", ControlCommandNames.GetSnapshotV2),
        new("disable-network-faults", ControlCommandNames.SetNetworkFaults),
        new("connect-late-client", "network.connect(client-c)"),
        new("verify-late-join", ControlCommandNames.GetSnapshotV2),
        new("disconnect-actor", ControlCommandNames.Disconnect),
        new("reconnect-actor", "network.connect(client-a)"),
        new("verify-reconnect", ControlCommandNames.GetSnapshotV2),
        new("prepare-persistence-health-fixture", ControlCommandNames.PrepareHealthFixture),
        new("save-and-stop-world", ControlCommandNames.SaveWorld),
        new("reload-saved-world", ControlCommandNames.ReloadWorld),
        new("restart-transport-host", "network.startHost"),
        new("reconnect-persisted-nodes", "network.connect(client-a, client-c)"),
        new("verify-persistence", ControlCommandNames.GetSnapshotV2)
    ];

    private sealed record InventoryExpectation(
        int PlayerInventoryId,
        int ChestInventoryId,
        int PlayerSlot0Count,
        int PlayerSlot1Count,
        int ChestSlot0Count,
        int ActiveSlotIndex,
        bool IncludePlayerInventory = true);

    public static NetworkFaultProfile CreateDefaultNetworkFaults(int seed) =>
        new(
            Seed: unchecked(seed + 907),
            BaseLatencyMilliseconds: 18,
            JitterMilliseconds: 4,
            DuplicationRate: 1,
            ReorderRate: 0.5,
            ReorderDelayMilliseconds: 18);

    public static InventoryHealthReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        (string Role, string Command)[] expected = [
            ("client-a", ControlCommandNames.InventoryMove),
            ("client-a", ControlCommandNames.InventorySelectSlot),
            ("client-a", ControlCommandNames.InventoryDrop),
            ("server", ControlCommandNames.RunInventoryRevisionBurst),
            ("client-a", ControlCommandNames.PlayerHit),
            ("server", ControlCommandNames.RunHealthRevisionBurst),
            ("client-a", ControlCommandNames.PlayerRequestRespawn)
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
        InventoryHealthReplayPlan plan = new(
            DeserializeAction<InventoryMoveCommand>(records[0]),
            DeserializeAction<InventorySelectSlotCommand>(records[1]),
            DeserializeAction<InventoryDropCommand>(records[2]),
            DeserializeAction<RunInventoryRevisionBurstCommand>(records[3]),
            DeserializeAction<PlayerHitCommand>(records[4]),
            DeserializeAction<RunHealthRevisionBurstCommand>(records[5]),
            DeserializeAction<PlayerRequestRespawnCommand>(records[6]));
        ValidateReplayPlan(plan);
        return plan;
    }

    public async Task<InventoryHealthMultiplayerScenarioResult> RunAsync(
        InventoryHealthMultiplayerScenarioOptions options,
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
                "Inventory and health resilience requires a non-empty network fault profile.",
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
        InventoryFixture? inventoryFixture = null;
        HealthFixture? healthFixture = null;
        HealthFixture? persistenceHealthFixture = null;
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
                    ControlCommandNames.ResetEvents,
                    ControlCommandNames.Disconnect,
                    ControlCommandNames.SaveWorld,
                    ControlCommandNames.ReloadWorld,
                    ControlCommandNames.SetNetworkFaults,
                    ControlCommandNames.PrepareInventoryFixture,
                    ControlCommandNames.ApplyInventoryFixture,
                    ControlCommandNames.InventoryMove,
                    ControlCommandNames.InventorySelectSlot,
                    ControlCommandNames.InventoryDrop,
                    ControlCommandNames.RunInventoryRevisionBurst,
                    ControlCommandNames.PrepareHealthFixture,
                    ControlCommandNames.ApplyHealthFixture,
                    ControlCommandNames.SetHealthFixture,
                    ControlCommandNames.PlayerHit,
                    ControlCommandNames.PlayerRequestRespawn,
                    ControlCommandNames.RunHealthRevisionBurst
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.World,
                    AutomationCommandDomain.Network,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Inventory,
                    AutomationCommandDomain.Health
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                required,
                capabilities,
                $"InventoryHealth-{options.Transport}-{options.Seed}",
                options.Seed,
                initialClientCount: 2,
                stepTimeout,
                scenarioToken,
                creative: false).ConfigureAwait(false);
            int initialActorPlayerIndex = actorPlayerIndex;
            GameNodeProcess actorClient = cluster.Clients[0];
            GameNodeProcess observerClient = cluster.Clients[1];
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
                "inventory-health-control-authority-enforcement",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                "server-only fixtures and client-only actions reject the wrong node role"));

            inventoryFixture = await runner.RunAsync(
                "prepare-inventory-fixture",
                ControlCommandNames.PrepareInventoryFixture,
                token => cluster.Server.SendAsync<PrepareInventoryFixtureCommand, InventoryFixture>(
                    ControlCommandNames.PrepareInventoryFixture,
                    new PrepareInventoryFixtureCommand(actorPlayerIndex),
                    timeout: stepTimeout,
                    cancellationToken: token),
                fixture => $"playerInventory={fixture.PlayerInventoryId}; chestInventory={fixture.ChestInventoryId}",
                scenarioToken).ConfigureAwait(false);
            ValidateInventoryFixture(inventoryFixture, actorPlayerIndex);
            _ = await runner.RunAsync(
                "apply-inventory-fixture",
                ControlCommandNames.ApplyInventoryFixture,
                token => actorClient.SendAsync<ApplyInventoryFixtureCommand, ControlAck>(
                    ControlCommandNames.ApplyInventoryFixture,
                    new ApplyInventoryFixtureCommand(inventoryFixture),
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);

            InventoryExpectation baselineInventory = new(
                inventoryFixture.PlayerInventoryId,
                inventoryFixture.ChestInventoryId,
                inventoryFixture.PlayerItemCount,
                3,
                inventoryFixture.ChestItemCount,
                0);
            Dictionary<string, StateSnapshotV2> baselineSnapshots =
                await WaitForInventoryOnAllAsync(
                    initialNodes,
                    baselineInventory,
                    requireStaleEvidence: false,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, baselineSnapshots, "inventory-baseline");
            EnsureInventorySnapshotsEqual(baselineSnapshots.Values.ToArray());

            ActionReceipt forgedMove = await runner.RunAsync(
                "verify-production-inventory-authority",
                ControlCommandNames.InventoryMove,
                token => observerClient.SendAsync<InventoryMoveCommand, ActionReceipt>(
                    ControlCommandNames.InventoryMove,
                    new InventoryMoveCommand(
                        actorPlayerIndex,
                        inventoryFixture.PlayerInventoryId,
                        0,
                        inventoryFixture.ChestInventoryId,
                        0,
                        1,
                        ExpectRejected: true),
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            EnsureAccepted(forgedMove);
            events["observer-forged-move"] = await WaitForObservedEventAsync(
                observerClient,
                forgedMove.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-after-forged-move"] = await WaitForUnchangedInventoryAsync(
                cluster.Server,
                baselineInventory,
                TimeSpan.FromMilliseconds(450),
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "inventory-session-ownership-enforcement",
                "passed",
                [cluster.Server.Role, observerClient.Role],
                "observer packet reached production transport but could not mutate actor or chest inventory"));

            await runner.RunAsync(
                "enable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(initialNodes, faultProfile, token),
                scenarioToken).ConfigureAwait(false);

            InventoryHealthReplayPlan plan = options.ReplayPlan
                ?? CreateDefaultPlan(actorPlayerIndex, inventoryFixture, targetEntityId: -1);
            InventoryMoveCommand move = plan.Move;
            ValidateInventoryPlan(move, plan.SelectSlot, plan.Drop, plan.InventoryRevisionBurst, inventoryFixture);

            ActionReceipt moveReceipt = await SendActionAsync(
                runner,
                "move-inventory-items",
                actorClient,
                ControlCommandNames.InventoryMove,
                move,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.InventoryMove, move, moveReceipt));
            events["client-a-inventory-move"] = await WaitForAppliedEventAsync(
                actorClient, moveReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            ActionReceipt selectReceipt = await SendActionAsync(
                runner,
                "select-inventory-slot",
                actorClient,
                ControlCommandNames.InventorySelectSlot,
                plan.SelectSlot,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(
                actorClient.Role,
                ControlCommandNames.InventorySelectSlot,
                plan.SelectSlot,
                selectReceipt));
            events["client-a-select-slot"] = await WaitForAppliedEventAsync(
                actorClient, selectReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            ActionReceipt dropReceipt = await SendActionAsync(
                runner,
                "drop-active-stack",
                actorClient,
                ControlCommandNames.InventoryDrop,
                plan.Drop,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.InventoryDrop, plan.Drop, dropReceipt));
            events["client-a-drop"] = await WaitForAppliedEventAsync(
                actorClient, dropReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);

            InventoryRevisionBurstResult inventoryBurst = await runner.RunAsync(
                "run-inventory-revision-burst",
                ControlCommandNames.RunInventoryRevisionBurst,
                token => cluster.Server.SendAsync<
                    RunInventoryRevisionBurstCommand,
                    InventoryRevisionBurstResult>(
                    ControlCommandNames.RunInventoryRevisionBurst,
                    plan.InventoryRevisionBurst,
                    cancellationToken: token),
                result => $"revision={result.FinalRevision}; count={result.FinalCount}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(inventoryBurst.Receipt);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.RunInventoryRevisionBurst,
                plan.InventoryRevisionBurst,
                inventoryBurst.Receipt));
            events["server-inventory-revision-burst"] = await WaitForAppliedEventAsync(
                cluster.Server,
                inventoryBurst.Receipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            InventoryExpectation finalInventory = new(
                inventoryFixture.PlayerInventoryId,
                inventoryFixture.ChestInventoryId,
                inventoryFixture.PlayerItemCount - move.Count,
                0,
                inventoryBurst.FinalCount,
                1);
            Dictionary<string, StateSnapshotV2> inventoryFinalSnapshots =
                await WaitForInventoryOnAllAsync(
                    initialNodes,
                    finalInventory,
                    requireStaleEvidence: true,
                    stepTimeout,
                    scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, inventoryFinalSnapshots, "inventory-final");
            EnsureInventorySnapshotsEqual(inventoryFinalSnapshots.Values.ToArray());
            assertions.Add(new ScenarioAssertionRecordV2(
                "inventory-duplicate-reorder-idempotent-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"moveCount={move.Count}; chestCount={inventoryBurst.FinalCount}; revision={inventoryBurst.FinalRevision}"));

            healthFixture = await runner.RunAsync(
                "prepare-health-fixture",
                ControlCommandNames.PrepareHealthFixture,
                token => cluster.Server.SendAsync<PrepareHealthFixtureCommand, HealthFixture>(
                    ControlCommandNames.PrepareHealthFixture,
                    new PrepareHealthFixtureCommand(actorPlayerIndex, TargetHealth: 0.01),
                    timeout: stepTimeout,
                    cancellationToken: token),
                fixture => $"target={fixture.TargetEntityId}; health={fixture.TargetHealth:R}",
                scenarioToken).ConfigureAwait(false);
            ValidateHealthFixture(healthFixture, actorPlayerIndex);
            _ = await runner.RunAsync(
                "apply-health-fixture",
                ControlCommandNames.ApplyHealthFixture,
                token => actorClient.SendAsync<ApplyHealthFixtureCommand, ControlAck>(
                    ControlCommandNames.ApplyHealthFixture,
                    new ApplyHealthFixtureCommand(healthFixture),
                    cancellationToken: token),
                cancellationToken: scenarioToken).ConfigureAwait(false);
            _ = await WaitForHealthOnAllAsync(
                initialNodes,
                healthFixture.TargetEntityId,
                state => state.Exists && !state.IsDead && IsNear(state.Health, healthFixture.TargetHealth, 0.001),
                requireStaleEvidence: false,
                compareRevisions: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            ActionReceipt forgedHit = await SendActionAsync(
                runner,
                "verify-production-hit-authority",
                observerClient,
                ControlCommandNames.PlayerHit,
                new PlayerHitCommand(
                    observerPlayerIndex,
                    healthFixture.TargetEntityId,
                    ExpectRejected: true),
                scenarioToken).ConfigureAwait(false);
            events["observer-forged-hit"] = await WaitForObservedEventAsync(
                observerClient,
                forgedHit.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-after-forged-hit"] = await WaitForUnchangedHealthAsync(
                cluster.Server,
                healthFixture.TargetEntityId,
                state => state.Exists
                    && !state.IsDead
                    && IsNear(state.Health, healthFixture.TargetHealth, 0.001),
                TimeSpan.FromMilliseconds(450),
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "health-server-authoritative-hit-validation",
                "passed",
                [cluster.Server.Role, observerClient.Role],
                "forged finite hit geometry reached production transport but could not injure the target"));

            plan = options.ReplayPlan
                ?? CreateDefaultPlan(actorPlayerIndex, inventoryFixture, healthFixture.TargetEntityId);
            ValidatePlanAgainstFixtures(plan, inventoryFixture, healthFixture);

            ActionReceipt hitReceipt = await SendActionAsync(
                runner,
                "hit-health-target",
                actorClient,
                ControlCommandNames.PlayerHit,
                plan.Hit,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(actorClient.Role, ControlCommandNames.PlayerHit, plan.Hit, hitReceipt));
            events["client-a-hit"] = await WaitForAppliedEventAsync(
                actorClient, hitReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);
            Dictionary<string, StateSnapshotV2> killedTargetSnapshots = await WaitForHealthOnAllAsync(
                initialNodes,
                healthFixture.TargetEntityId,
                state => state.Exists
                    && state.IsDead
                    && state.Health <= 0
                    && state.HasDeathTime,
                requireStaleEvidence: false,
                compareRevisions: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, killedTargetSnapshots, "target-after-hit");
            EnsureHealthSnapshotsEqual(killedTargetSnapshots.Values.ToArray());
            assertions.Add(new ScenarioAssertionRecordV2(
                "health-authoritative-hit-death-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"targetEntityId={healthFixture.TargetEntityId}; duplicate hit executed once"));

            HealthRevisionBurstResult healthBurst = await runner.RunAsync(
                "run-health-revision-burst",
                ControlCommandNames.RunHealthRevisionBurst,
                token => cluster.Server.SendAsync<
                    RunHealthRevisionBurstCommand,
                    HealthRevisionBurstResult>(
                    ControlCommandNames.RunHealthRevisionBurst,
                    plan.HealthRevisionBurst,
                    cancellationToken: token),
                result => $"revision={result.FinalRevision}; health={result.FinalHealth:R}",
                scenarioToken).ConfigureAwait(false);
            EnsureAccepted(healthBurst.Receipt);
            actions.Add(ActionRecord(
                cluster.Server.Role,
                ControlCommandNames.RunHealthRevisionBurst,
                plan.HealthRevisionBurst,
                healthBurst.Receipt));
            events["server-health-revision-burst"] = await WaitForAppliedEventAsync(
                cluster.Server,
                healthBurst.Receipt.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            Dictionary<string, StateSnapshotV2> healthFinalSnapshots = await WaitForHealthOnAllAsync(
                initialNodes,
                healthFixture.TargetEntityId,
                state => state.Exists
                    && !state.IsDead
                    && IsNear(state.Health, healthBurst.FinalHealth, 0.001),
                requireStaleEvidence: true,
                compareRevisions: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, healthFinalSnapshots, "health-revision-final");
            EnsureHealthSnapshotsEqual(healthFinalSnapshots.Values.ToArray());
            assertions.Add(new ScenarioAssertionRecordV2(
                "health-stale-duplicate-reorder-convergence",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"targetEntityId={healthFixture.TargetEntityId}; revision={healthBurst.FinalRevision}"));

            int deadActorEntityId = healthFixture.PlayerEntityId;
            SetHealthFixtureResult actorDeath = await runner.RunAsync(
                "kill-actor",
                ControlCommandNames.SetHealthFixture,
                token => cluster.Server.SendAsync<SetHealthFixtureCommand, SetHealthFixtureResult>(
                    ControlCommandNames.SetHealthFixture,
                    new SetHealthFixtureCommand(deadActorEntityId, Kill: true, Cause: "AutoTestRespawn"),
                    cancellationToken: token),
                result => $"entity={result.EntityId}; revision={result.Revision}",
                scenarioToken).ConfigureAwait(false);
            if (!actorDeath.IsDead) {
                throw new InvalidOperationException("The server health fixture did not kill the actor.");
            }
            _ = await WaitForHealthOnAllAsync(
                initialNodes,
                deadActorEntityId,
                state => state.Exists && state.IsDead,
                requireStaleEvidence: false,
                compareRevisions: true,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            ActionReceipt forgedRespawn = await SendActionAsync(
                runner,
                "verify-production-respawn-authority",
                observerClient,
                ControlCommandNames.PlayerRequestRespawn,
                new PlayerRequestRespawnCommand(
                    actorPlayerIndex,
                    ExpectRejected: true),
                scenarioToken).ConfigureAwait(false);
            events["observer-forged-respawn"] = await WaitForObservedEventAsync(
                observerClient,
                forgedRespawn.ActionId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            snapshots["server-after-forged-respawn"] = await WaitForUnchangedHealthAsync(
                cluster.Server,
                deadActorEntityId,
                state => state.Exists && state.IsDead && state.Health <= 0,
                TimeSpan.FromMilliseconds(450),
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "respawn-session-ownership-enforcement",
                "passed",
                [cluster.Server.Role, observerClient.Role],
                "observer impersonation packet reached production transport but actor remained dead"));

            ActionReceipt respawnReceipt = await SendActionAsync(
                runner,
                "request-respawn",
                actorClient,
                ControlCommandNames.PlayerRequestRespawn,
                plan.Respawn,
                scenarioToken).ConfigureAwait(false);
            actions.Add(ActionRecord(
                actorClient.Role,
                ControlCommandNames.PlayerRequestRespawn,
                plan.Respawn,
                respawnReceipt));
            events["client-a-respawn"] = await WaitForAppliedEventAsync(
                actorClient, respawnReceipt.ActionId, stepTimeout, scenarioToken).ConfigureAwait(false);
            Dictionary<string, StateSnapshotV2> respawnSnapshots = await WaitForRespawnOnAllAsync(
                initialNodes,
                actorPlayerIndex,
                deadActorEntityId,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, respawnSnapshots, "after-respawn");
            int respawnedEntityId = RequireReadyActor(
                respawnSnapshots[cluster.Server.Role],
                actorPlayerIndex).EntityId!.Value;
            _ = await WaitForStableRespawnAsync(
                cluster.Server,
                actorPlayerIndex,
                respawnedEntityId,
                TimeSpan.FromMilliseconds(500),
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "player-death-respawn-duplicate-idempotency",
                "passed",
                initialNodes.Select(node => node.Role).ToArray(),
                $"oldEntityId={deadActorEntityId}; newEntityId={respawnedEntityId}; playerIndex={actorPlayerIndex}"));

            long duplicatedPackets = respawnSnapshots.Values
                .Where(snapshot => snapshot.Network != null)
                .Sum(snapshot => snapshot.Network!.FaultDuplicated);
            if (duplicatedPackets <= 0) {
                throw new InvalidOperationException(
                    "The deterministic fault injector did not duplicate inventory/health packets.");
            }
            metrics["fault.duplicatedPackets"] = duplicatedPackets.ToString(CultureInfo.InvariantCulture);
            metrics["fault.profile"] = FormatFaultProfile(faultProfile);

            await runner.RunAsync(
                "disable-network-faults",
                ControlCommandNames.SetNetworkFaults,
                token => ConfigureNetworkFaultsAsync(
                    initialNodes,
                    new NetworkFaultProfile(Seed: unchecked(faultProfile.Seed + 100)),
                    token),
                scenarioToken).ConfigureAwait(false);

            InventoryExpectation chestOnly = finalInventory with { IncludePlayerInventory = false };
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
            snapshots["client-c-after-late-join"] = await WaitForWorldBaselineAsync(
                lateClient,
                actorPlayerIndex,
                chestOnly,
                healthFixture.TargetEntityId,
                healthBurst.FinalHealth,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "inventory-health-late-join-baseline",
                "passed",
                [lateClient.Role],
                "late joiner received current chest, health, and respawned player baselines"));

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
                        "Client A did not return to MainMenu.",
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
            EnsureActorIdentity(reconnectStatus, initialActorPlayerIndex, "reconnect");
            snapshots["client-a-after-reconnect"] = await WaitForWorldBaselineAsync(
                actorClient,
                actorPlayerIndex,
                chestOnly,
                healthFixture.TargetEntityId,
                healthBurst.FinalHealth,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "inventory-health-reconnect-baseline",
                "passed",
                [cluster.Server.Role, actorClient.Role],
                $"playerIndex={actorPlayerIndex}; chest and health baselines restored"));

            persistenceHealthFixture = await runner.RunAsync(
                "prepare-persistence-health-fixture",
                ControlCommandNames.PrepareHealthFixture,
                token => cluster.Server.SendAsync<PrepareHealthFixtureCommand, HealthFixture>(
                    ControlCommandNames.PrepareHealthFixture,
                    new PrepareHealthFixtureCommand(
                        actorPlayerIndex,
                        TargetHealth: healthBurst.FinalHealth),
                    cancellationToken: token),
                fixture => $"targetEntityId={fixture.TargetEntityId}; health={fixture.TargetHealth:R}",
                scenarioToken).ConfigureAwait(false);
            GameNodeProcess[] preSaveNodes = [
                cluster.Server,
                actorClient,
                observerClient,
                lateClient
            ];
            Dictionary<string, StateSnapshotV2> preSaveHealthSnapshots = await WaitForHealthOnAllAsync(
                preSaveNodes,
                persistenceHealthFixture.TargetEntityId,
                state => state.Exists
                    && !state.IsDead
                    && IsNear(state.Health, persistenceHealthFixture.TargetHealth, 0.001),
                requireStaleEvidence: false,
                compareRevisions: false,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);
            AddSnapshots(snapshots, preSaveHealthSnapshots, "persistence-health-baseline");
            EnsureHealthSnapshotsEqual(
                preSaveHealthSnapshots.Values.ToArray(),
                compareRevisions: false);
            assertions.Add(new ScenarioAssertionRecordV2(
                "stable-health-persistence-baseline",
                "passed",
                preSaveNodes.Select(node => node.Role).ToArray(),
                $"targetEntityId={persistenceHealthFixture.TargetEntityId}; health={persistenceHealthFixture.TargetHealth:R}"));

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
            NodeStatus persistedActor = await actorClient.GetStatusAsync(scenarioToken).ConfigureAwait(false);
            EnsureActorIdentity(persistedActor, initialActorPlayerIndex, "save/reload");
            GameNodeProcess[] persistenceNodes = [cluster.Server, actorClient, lateClient];
            Dictionary<string, StateSnapshotV2> persistenceSnapshots = new(StringComparer.Ordinal);
            foreach (GameNodeProcess node in persistenceNodes) {
                persistenceSnapshots[node.Role] = await WaitForWorldBaselineAsync(
                    node,
                    actorPlayerIndex,
                    chestOnly,
                    persistenceHealthFixture.TargetEntityId,
                    persistenceHealthFixture.TargetHealth,
                    stepTimeout,
                    scenarioToken,
                    healthTolerance: 0.05).ConfigureAwait(false);
            }
            AddSnapshots(snapshots, persistenceSnapshots, "after-reload");
            EnsureInventorySnapshotsEqual(persistenceSnapshots.Values.ToArray());
            EnsureHealthSnapshotsEqual(
                persistenceSnapshots.Values.ToArray(),
                compareRevisions: false,
                scalarTolerance: 0.05);
            assertions.Add(new ScenarioAssertionRecordV2(
                "inventory-health-save-reload-persistence",
                "passed",
                persistenceNodes.Select(node => node.Role).ToArray(),
                $"world={savedWorldDirectory}; chestCount={chestOnly.ChestSlot0Count}; "
                    + $"targetEntityId={persistenceHealthFixture.TargetEntityId}; "
                    + $"targetHealth={persistenceHealthFixture.TargetHealth:R}"));
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(
                    cluster,
                    inventoryFixture,
                    healthFixture,
                    persistenceHealthFixture,
                    actorPlayerIndex,
                    snapshots,
                    events).ConfigureAwait(false);
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
            throw new InventoryHealthMultiplayerScenarioException(
                $"Inventory/health multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new InventoryHealthMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new InventoryHealthMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private static InventoryHealthReplayPlan CreateDefaultPlan(
        int playerIndex,
        InventoryFixture fixture,
        int targetEntityId) =>
        new(
            new InventoryMoveCommand(
                playerIndex,
                fixture.PlayerInventoryId,
                0,
                fixture.ChestInventoryId,
                0,
                3),
            new InventorySelectSlotCommand(playerIndex, fixture.PlayerInventoryId, 1),
            new InventoryDropCommand(playerIndex),
            new RunInventoryRevisionBurstCommand(fixture.ChestInventoryId, 0),
            new PlayerHitCommand(playerIndex, targetEntityId),
            new RunHealthRevisionBurstCommand(targetEntityId),
            new PlayerRequestRespawnCommand(playerIndex));

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
                    () => cluster.Clients[0].SendAsync<PrepareInventoryFixtureCommand, InventoryFixture>(
                        ControlCommandNames.PrepareInventoryFixture,
                        new PrepareInventoryFixtureCommand(playerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunInventoryRevisionBurstCommand, InventoryRevisionBurstResult>(
                        ControlCommandNames.RunInventoryRevisionBurst,
                        new RunInventoryRevisionBurstCommand(1, 0),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[0].SendAsync<PrepareHealthFixtureCommand, HealthFixture>(
                        ControlCommandNames.PrepareHealthFixture,
                        new PrepareHealthFixtureCommand(playerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Clients[1].SendAsync<RunHealthRevisionBurstCommand, HealthRevisionBurstResult>(
                        ControlCommandNames.RunHealthRevisionBurst,
                        new RunHealthRevisionBurstCommand(1),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<InventoryDropCommand, ActionReceipt>(
                        ControlCommandNames.InventoryDrop,
                        new InventoryDropCommand(playerIndex),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
                await RequireRejectionAsync(
                    () => cluster.Server.SendAsync<PlayerHitCommand, ActionReceipt>(
                        ControlCommandNames.PlayerHit,
                        new PlayerHitCommand(playerIndex, 1),
                        cancellationToken: token),
                    "permission_denied").ConfigureAwait(false);
            },
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

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForInventoryOnAllAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        InventoryExpectation expected,
        bool requireStaleEvidence,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Dictionary<string, StateSnapshotV2> result = new(StringComparer.Ordinal);
        foreach (GameNodeProcess node in nodes) {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) {
                throw new TimeoutException("Inventory cluster convergence exceeded its deadline.");
            }
            result[node.Role] = await WaitForInventoryAsync(
                node,
                expected,
                requireStaleEvidence: false,
                remaining,
                cancellationToken).ConfigureAwait(false);
        }
        while (requireStaleEvidence
            && !result.Any(pair =>
                pair.Key.StartsWith("client", StringComparison.Ordinal)
                && pair.Value.StateRevisions?.IgnoredStaleStates > 0)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline) {
                throw new TimeoutException(
                    "Inventory state converged, but no client observed a stale-state rejection.");
            }
            foreach (GameNodeProcess node in nodes.Where(node =>
                         node.Role.StartsWith("client", StringComparison.Ordinal))) {
                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                result[node.Role] = await WaitForInventoryAsync(
                    node,
                    expected,
                    requireStaleEvidence: false,
                    remaining,
                    cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private static async Task<StateSnapshotV2> WaitForInventoryAsync(
        GameNodeProcess node,
        InventoryExpectation expected,
        bool requireStaleEvidence,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureAsync(
                node,
                expected.IncludePlayerInventory
                    ? [expected.PlayerInventoryId, expected.ChestInventoryId]
                    : [expected.ChestInventoryId],
                [],
                [],
                cancellationToken).ConfigureAwait(false);
            InventorySnapshotV2? chest = FindInventory(last, expected.ChestInventoryId);
            InventorySnapshotV2? player = expected.IncludePlayerInventory
                ? FindInventory(last, expected.PlayerInventoryId)
                : null;
            bool matches = chest != null
                && SlotCount(chest, 0) == expected.ChestSlot0Count
                && (!expected.IncludePlayerInventory
                    || player != null
                        && SlotCount(player, 0) == expected.PlayerSlot0Count
                        && SlotCount(player, 1) == expected.PlayerSlot1Count
                        && player.ActiveSlotIndex == expected.ActiveSlotIndex)
                && (!requireStaleEvidence || last.StateRevisions?.IgnoredStaleStates > 0);
            if (matches) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge inventory state. Last={FormatInventories(last)}.");
    }

    private static async Task<StateSnapshotV2> WaitForUnchangedInventoryAsync(
        GameNodeProcess node,
        InventoryExpectation expected,
        TimeSpan minimumStableDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        DateTimeOffset? stableSince = null;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            last = await WaitForInventoryAsync(
                node,
                expected,
                requireStaleEvidence: false,
                TimeSpan.FromSeconds(1),
                cancellationToken).ConfigureAwait(false);
            stableSince ??= DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - stableSince >= minimumStableDuration) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' inventory did not remain unchanged after the forged request.");
    }

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForHealthOnAllAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        int entityId,
        Func<HealthStateSnapshotV2, bool> predicate,
        bool requireStaleEvidence,
        bool compareRevisions,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Dictionary<string, StateSnapshotV2> last = new(StringComparer.Ordinal);
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            Task<StateSnapshotV2>[] captures = nodes
                .Select(node => CaptureAsync(node, [], [entityId], [], cancellationToken))
                .ToArray();
            StateSnapshotV2[] values = await Task.WhenAll(captures).ConfigureAwait(false);
            last.Clear();
            int index = 0;
            bool allReady = true;
            foreach (GameNodeProcess node in nodes) {
                StateSnapshotV2 snapshot = values[index++];
                last[node.Role] = snapshot;
                HealthStateSnapshotV2? state = snapshot.HealthStates?.SingleOrDefault();
                if (state == null
                    || !predicate(state)) {
                    allReady = false;
                }
            }
            if (allReady) {
                StateSnapshotV2 expected = values[0];
                SnapshotV2HealthComparisonOptions comparisonOptions = new(
                    CompareRevisions: compareRevisions);
                bool converged = values.Skip(1).All(actual =>
                    SnapshotV2Comparer.CompareHealthStates(
                        expected,
                        actual,
                        comparisonOptions).Equal);
                bool staleEvidenceObserved = !requireStaleEvidence
                    || last.Any(pair =>
                        pair.Key.StartsWith("client", StringComparison.Ordinal)
                        && pair.Value.StateRevisions?.IgnoredStaleStates > 0);
                if (converged && staleEvidenceObserved) {
                    return new Dictionary<string, StateSnapshotV2>(last, StringComparer.Ordinal);
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        string lastStates = string.Join(
            "; ",
            last.Select(pair => $"{pair.Key}={pair.Value.HealthStates?.SingleOrDefault()}"));
        throw new TimeoutException(
            $"Nodes did not converge health entity {entityId}. Last={lastStates}.");
    }

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForRespawnOnAllAsync(
        IReadOnlyCollection<GameNodeProcess> nodes,
        int playerIndex,
        int deadEntityId,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        Dictionary<string, StateSnapshotV2> result = new(StringComparer.Ordinal);
        foreach (GameNodeProcess node in nodes) {
            result[node.Role] = await WaitForReadyActorAsync(
                node,
                playerIndex,
                player => player.EntityId.HasValue
                    && player.EntityId.Value != deadEntityId
                    && player.Health > 0,
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        int[] entityIds = result.Values
            .Select(snapshot => RequireReadyActor(snapshot, playerIndex).EntityId!.Value)
            .Distinct()
            .ToArray();
        if (entityIds.Length != 1) {
            throw new InvalidOperationException(
                $"Respawned actor entity IDs diverged: {string.Join(", ", entityIds)}.");
        }
        return result;
    }

    private static async Task<StateSnapshotV2> WaitForUnchangedHealthAsync(
        GameNodeProcess node,
        int entityId,
        Func<HealthStateSnapshotV2, bool> predicate,
        TimeSpan minimumStableDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        DateTimeOffset? stableSince = null;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureAsync(node, [], [entityId], [], cancellationToken)
                .ConfigureAwait(false);
            HealthStateSnapshotV2? state = last.HealthStates?
                .SingleOrDefault(value => value.EntityId == entityId);
            if (state != null && predicate(state)) {
                stableSince ??= DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - stableSince >= minimumStableDuration) {
                    return last;
                }
            }
            else {
                stableSince = null;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' health for entity {entityId} did not remain unchanged after a forged request. Last={last?.HealthStates?.SingleOrDefault()}.");
    }

    private static async Task<StateSnapshotV2> WaitForStableRespawnAsync(
        GameNodeProcess node,
        int playerIndex,
        int entityId,
        TimeSpan minimumStableDuration,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        DateTimeOffset? stableSince = null;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            last = await CaptureAsync(node, [], [entityId], [playerIndex], cancellationToken)
                .ConfigureAwait(false);
            PlayerSnapshotV2? actor = last.Players.SingleOrDefault(player => player.PlayerIndex == playerIndex);
            if (actor?.EntityId == entityId && actor.ReadyForPlaying && actor.Health > 0) {
                stableSince ??= DateTimeOffset.UtcNow;
                if (DateTimeOffset.UtcNow - stableSince >= minimumStableDuration) {
                    return last;
                }
            }
            else {
                stableSince = null;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not keep the respawned entity stable after duplicate packets.");
    }

    private static async Task<StateSnapshotV2> WaitForReadyActorAsync(
        GameNodeProcess node,
        int playerIndex,
        Func<PlayerSnapshotV2, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            last = await CaptureAsync(node, [], [], [playerIndex], cancellationToken)
                .ConfigureAwait(false);
            PlayerSnapshotV2? actor = last.Players.SingleOrDefault(player => player.PlayerIndex == playerIndex);
            if (actor != null && actor.ReadyForPlaying && predicate(actor)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose ready player {playerIndex}. Last={last?.Players.SingleOrDefault()}.");
    }

    private static async Task<StateSnapshotV2> WaitForWorldBaselineAsync(
        GameNodeProcess node,
        int actorPlayerIndex,
        InventoryExpectation inventory,
        int healthEntityId,
        double expectedHealth,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        double healthTolerance = 0.001) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureAsync(
                node,
                [inventory.ChestInventoryId],
                [healthEntityId],
                [actorPlayerIndex],
                cancellationToken).ConfigureAwait(false);
            InventorySnapshotV2? chest = FindInventory(last, inventory.ChestInventoryId);
            HealthStateSnapshotV2? health = last.HealthStates?.SingleOrDefault();
            PlayerSnapshotV2? actor = last.Players.SingleOrDefault(
                player => player.PlayerIndex == actorPlayerIndex);
            if (chest != null
                && SlotCount(chest, 0) == inventory.ChestSlot0Count
                && health != null
                && health.Exists
                && !health.IsDead
                && IsNear(health.Health, expectedHealth, healthTolerance)
                && actor?.ReadyForPlaying == true
                && actor.EntityId.HasValue
                && actor.Health > 0) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not expose the inventory/health/player baseline. "
            + $"Inventories={FormatInventories(last)}; health={last?.HealthStates?.SingleOrDefault()}; "
            + $"actor={last?.Players.SingleOrDefault()}.");
    }

    private static Task<StateSnapshotV2> CaptureAsync(
        GameNodeProcess node,
        int[] inventoryIds,
        int[] entityIds,
        int[] playerIndexes,
        CancellationToken cancellationToken) =>
        node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [
                    SnapshotDomain.Network,
                    SnapshotDomain.Players,
                    SnapshotDomain.Inventories,
                    SnapshotDomain.Health
                ],
                PlayerIndexes: playerIndexes,
                EntityIds: entityIds,
                InventoryIds: inventoryIds),
            cancellationToken: cancellationToken);

    private static async Task<ActionEventsResult> WaitForAppliedEventAsync(
        GameNodeProcess node,
        string actionId,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await WaitForEventAsync(
            node,
            actionId,
            events => events.Any(item => item.Phase == ActionEventPhase.Queued)
                && events.Any(item => item.Phase == ActionEventPhase.Applied),
            timeout,
            cancellationToken).ConfigureAwait(false);

    private static async Task<ActionEventsResult> WaitForObservedEventAsync(
        GameNodeProcess node,
        string actionId,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await WaitForEventAsync(
            node,
            actionId,
            events => events.Any(item => item.Phase == ActionEventPhase.Queued)
                && events.Any(item => item.Phase == ActionEventPhase.Observed),
            timeout,
            cancellationToken).ConfigureAwait(false);

    private static async Task<ActionEventsResult> WaitForEventAsync(
        GameNodeProcess node,
        string actionId,
        Func<ActionEventRecord[], bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        ActionEventsResult? last = null;
        while (DateTimeOffset.UtcNow < deadline) {
            last = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
                ControlCommandNames.GetEvents,
                new GetEventsCommand(Limit: AutomationProtocol.MaximumEventQueryCount),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            ActionEventRecord[] selected = last.Events
                .Where(item => string.Equals(item.ActionId, actionId, StringComparison.Ordinal))
                .ToArray();
            ActionEventRecord? rejected = selected.FirstOrDefault(
                item => item.Phase == ActionEventPhase.Rejected);
            if (rejected != null) {
                throw new InvalidOperationException(
                    $"Action '{actionId}' was rejected: {rejected.Code}.");
            }
            if (predicate(selected)) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Node '{node.Role}' did not publish the expected events for '{actionId}'. Last={last}.");
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
                        "Server declined to save the inventory/health world.");
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
                    "Saved inventory/health world did not become playable.",
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return savedWorldDirectory;
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        InventoryFixture? inventoryFixture,
        HealthFixture? healthFixture,
        HealthFixture? persistenceHealthFixture,
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
                    inventoryFixture == null
                        ? []
                        : [inventoryFixture.PlayerInventoryId, inventoryFixture.ChestInventoryId],
                    healthFixture == null
                        ? []
                        : persistenceHealthFixture == null
                            ? [healthFixture.PlayerEntityId, healthFixture.TargetEntityId]
                            : [
                                healthFixture.PlayerEntityId,
                                healthFixture.TargetEntityId,
                                persistenceHealthFixture.TargetEntityId
                            ],
                    actorPlayerIndex < 0 ? [] : [actorPlayerIndex],
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Preserve the original scenario failure.
            }
            try {
                events[$"{node.Role}-final"] = await node.SendAsync<GetEventsCommand, ActionEventsResult>(
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

    private static void EnsureInventorySnapshotsEqual(IReadOnlyList<StateSnapshotV2> values) {
        StateSnapshotV2 expected = values[0];
        foreach (StateSnapshotV2 actual in values.Skip(1)) {
            SnapshotV2Diff difference = SnapshotV2Comparer.CompareInventories(expected, actual);
            if (!difference.Equal) {
                throw new InvalidOperationException(
                    $"Inventory snapshots diverged: {string.Join(", ", difference.Differences.Select(item => item.Path))}.");
            }
        }
    }

    private static void EnsureHealthSnapshotsEqual(
        IReadOnlyList<StateSnapshotV2> values,
        bool compareRevisions = true,
        double scalarTolerance = 0.001) {
        StateSnapshotV2 expected = values[0];
        SnapshotV2HealthComparisonOptions options = new(
            ScalarTolerance: scalarTolerance,
            CompareRevisions: compareRevisions);
        foreach (StateSnapshotV2 actual in values.Skip(1)) {
            SnapshotV2Diff difference = SnapshotV2Comparer.CompareHealthStates(
                expected,
                actual,
                options);
            if (!difference.Equal) {
                throw new InvalidOperationException(
                    $"Health snapshots diverged: {string.Join(", ", difference.Differences.Select(item => item.Path))}.");
            }
        }
    }

    private static InventorySnapshotV2? FindInventory(StateSnapshotV2 snapshot, int inventoryId) =>
        snapshot.Inventories?.SingleOrDefault(inventory => inventory.InventoryId == inventoryId);

    private static int SlotCount(InventorySnapshotV2 inventory, int slotIndex) =>
        inventory.Slots.Single(slot => slot.SlotIndex == slotIndex).Count;

    private static PlayerSnapshotV2 RequireReadyActor(StateSnapshotV2 snapshot, int playerIndex) =>
        snapshot.Players.Single(player => player.PlayerIndex == playerIndex);

    private static bool IsNear(double? actual, double expected, double tolerance) =>
        actual.HasValue && Math.Abs(actual.Value - expected) <= tolerance;

    private static void ValidateInventoryFixture(InventoryFixture fixture, int playerIndex) {
        if (fixture.PlayerIndex != playerIndex
            || fixture.PlayerEntityId <= 0
            || fixture.PlayerInventoryId <= 0
            || fixture.ChestEntityId <= 0
            || fixture.ChestInventoryId <= 0
            || fixture.PlayerInventoryId == fixture.ChestInventoryId
            || fixture.PlayerItemCount < 1) {
            throw new InvalidOperationException($"Invalid inventory fixture: {fixture}.");
        }
    }

    private static void ValidateHealthFixture(HealthFixture fixture, int playerIndex) {
        if (fixture.PlayerIndex != playerIndex
            || fixture.PlayerEntityId <= 0
            || fixture.TargetEntityId <= 0
            || fixture.PlayerEntityId == fixture.TargetEntityId
            || fixture.TargetHealth is <= 0 or > 1) {
            throw new InvalidOperationException($"Invalid health fixture: {fixture}.");
        }
    }

    private static void ValidateInventoryPlan(
        InventoryMoveCommand move,
        InventorySelectSlotCommand select,
        InventoryDropCommand drop,
        RunInventoryRevisionBurstCommand burst,
        InventoryFixture fixture) {
        if (move.PlayerIndex != fixture.PlayerIndex
            || move.FromInventoryId != fixture.PlayerInventoryId
            || move.ToInventoryId != fixture.ChestInventoryId
            || move.FromSlotIndex != 0
            || move.ToSlotIndex != 0
            || move.Count is < 1 or > 4
            || move.ExpectRejected
            || select.PlayerIndex != fixture.PlayerIndex
            || select.InventoryId != fixture.PlayerInventoryId
            || select.SlotIndex != 1
            || drop.PlayerIndex != fixture.PlayerIndex
            || burst.InventoryId != fixture.ChestInventoryId
            || burst.SlotIndex != 0
            || !burst.SendStaleSnapshot) {
            throw new InvalidDataException("Inventory replay actions do not match the prepared fixture.");
        }
    }

    private static void ValidatePlanAgainstFixtures(
        InventoryHealthReplayPlan plan,
        InventoryFixture inventory,
        HealthFixture health) {
        ValidateInventoryPlan(
            plan.Move,
            plan.SelectSlot,
            plan.Drop,
            plan.InventoryRevisionBurst,
            inventory);
        if (plan.Hit.PlayerIndex != health.PlayerIndex
            || plan.Hit.TargetEntityId != health.TargetEntityId
            || plan.Hit.ExpectRejected
            || plan.HealthRevisionBurst.EntityId != health.TargetEntityId
            || !plan.HealthRevisionBurst.SendStaleSnapshot
            || plan.Respawn.PlayerIndex != health.PlayerIndex
            || plan.Respawn.ExpectRejected) {
            throw new InvalidDataException("Health replay actions do not match the prepared fixture.");
        }
    }

    private static void ValidateReplayPlan(InventoryHealthReplayPlan? plan) {
        if (plan == null) {
            return;
        }
        if (plan.Move.Count <= 0
            || plan.Move.ExpectRejected
            || plan.SelectSlot.SlotIndex < 0
            || !plan.InventoryRevisionBurst.SendStaleSnapshot
            || plan.Hit.TargetEntityId <= 0
            || plan.Hit.ExpectRejected
            || plan.Respawn.ExpectRejected
            || !plan.HealthRevisionBurst.SendStaleSnapshot) {
            throw new InvalidDataException("Inventory/health replay actions are invalid.");
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
        ActionReceipt receipt) =>
        new(
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

    private static string FormatInventories(StateSnapshotV2? snapshot) =>
        snapshot?.Inventories == null
            ? "<none>"
            : string.Join(" | ", snapshot.Inventories.Select(inventory =>
                $"{inventory.InventoryId}@{inventory.Revision}:"
                + string.Join(",", inventory.Slots.Take(3).Select(slot => $"{slot.SlotIndex}={slot.Count}"))));

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
