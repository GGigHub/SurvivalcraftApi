using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record TerrainInteractionReplayPlan(
    PlayerUseCommand Use,
    PlayerPlaceCommand Place,
    PlayerDigCommand Dig);

public sealed record TerrainInteractionMultiplayerScenarioOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    int Seed,
    MultiplayerTransportKind Transport,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    string? WorkDirectory = null,
    bool KeepData = false,
    TerrainInteractionReplayPlan? ReplayPlan = null);

public sealed record TerrainInteractionMultiplayerScenarioResult(
    string ArtifactDirectory,
    MultiplayerTransportKind Transport,
    int ActorPlayerIndex,
    string? RoomCode,
    int ActionCount,
    TimeSpan Elapsed);

public sealed class TerrainInteractionMultiplayerScenarioException : Exception {
    public TerrainInteractionMultiplayerScenarioException(
        string message,
        string artifactDirectory,
        Exception innerException)
        : base(message, innerException) {
        ArtifactDirectory = artifactDirectory;
    }

    public string ArtifactDirectory { get; }
}

public sealed class TerrainInteractionMultiplayerScenario {
    public const string ScenarioName = "terrain-interaction-multiplayer";
    public const int ScenarioVersion = 1;

    private static readonly PlayerTerrainActionKind[] ActionOrder = [
        PlayerTerrainActionKind.Use,
        PlayerTerrainActionKind.Place,
        PlayerTerrainActionKind.Dig
    ];

    private static readonly ScenarioStepDefinition[] StepDefinitions = [
        new("start-cluster", "process.start(server, client-a, client-b, relay?)"),
        new("wait-main-menu", ControlCommandNames.GetStatus),
        new("require-v2-capabilities", ControlCommandNames.Describe),
        new("configure-determinism", ControlCommandNames.ConfigureNode),
        new("create-server-world", ControlCommandNames.CreateWorld),
        new("enter-server-world", ControlCommandNames.EnterWorld),
        new("start-transport-host", "network.startHost"),
        new("connect-two-clients", "network.connect(client-a, client-b)"),
        new("resolve-actor", ControlCommandNames.GetStatus),
        new("reject-server-originated-action", ControlCommandNames.PlayerUse),
        new("reject-unreachable-client-target", ControlCommandNames.PlayerUse),
        new("reset-events", ControlCommandNames.ResetEvents),
        new("prepare-use-fixture", ControlCommandNames.PrepareTerrainInteractionFixture),
        new("apply-use-fixture", ControlCommandNames.ApplyTerrainInteractionFixture),
        new("verify-use-baseline", ControlCommandNames.GetSnapshotV2),
        new("execute-use", ControlCommandNames.PlayerUse),
        new("verify-use-convergence", ControlCommandNames.GetSnapshotV2),
        new("verify-use-events", ControlCommandNames.GetEvents),
        new("prepare-place-fixture", ControlCommandNames.PrepareTerrainInteractionFixture),
        new("apply-place-fixture", ControlCommandNames.ApplyTerrainInteractionFixture),
        new("verify-place-baseline", ControlCommandNames.GetSnapshotV2),
        new("execute-place", ControlCommandNames.PlayerPlace),
        new("verify-place-convergence", ControlCommandNames.GetSnapshotV2),
        new("verify-place-events", ControlCommandNames.GetEvents),
        new("prepare-dig-fixture", ControlCommandNames.PrepareTerrainInteractionFixture),
        new("apply-dig-fixture", ControlCommandNames.ApplyTerrainInteractionFixture),
        new("verify-dig-baseline", ControlCommandNames.GetSnapshotV2),
        new("execute-dig", ControlCommandNames.PlayerDig),
        new("verify-dig-convergence", ControlCommandNames.GetSnapshotV2),
        new("verify-dig-events", ControlCommandNames.GetEvents)
    ];

    public static TerrainInteractionReplayPlan ParseReplayActions(
        IReadOnlyList<ScenarioActionRecordV2> records) {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != ActionOrder.Length) {
            throw new InvalidDataException(
                $"{ScenarioName} version {ScenarioVersion} requires exactly "
                + $"{ActionOrder.Length} recorded actions.");
        }
        string[] expectedCommands = [
            ControlCommandNames.PlayerUse,
            ControlCommandNames.PlayerPlace,
            ControlCommandNames.PlayerDig
        ];
        for (int index = 0; index < records.Count; index++) {
            ScenarioActionRecordV2 record = records[index];
            if (!string.Equals(record.Role, "client-a", StringComparison.Ordinal)) {
                throw new InvalidDataException(
                    $"Replay action {index} has unsupported role '{record.Role}'.");
            }
            if (!string.Equals(record.Command, expectedCommands[index], StringComparison.Ordinal)) {
                throw new InvalidDataException(
                    $"Replay action {index} must be '{expectedCommands[index]}', got '{record.Command}'.");
            }
        }
        return new TerrainInteractionReplayPlan(
            DeserializeAction<PlayerUseCommand>(records[0]),
            DeserializeAction<PlayerPlaceCommand>(records[1]),
            DeserializeAction<PlayerDigCommand>(records[2]));
    }

    public async Task<TerrainInteractionMultiplayerScenarioResult> RunAsync(
        TerrainInteractionMultiplayerScenarioOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        ValidateRuntime(runtimeDirectory, options.Transport);
        ValidateReplayPlan(options.ReplayPlan);

        TimeSpan stepTimeout = options.StepTimeout ?? TimeSpan.FromSeconds(60);
        TimeSpan scenarioTimeout = options.ScenarioTimeout ?? TimeSpan.FromMinutes(8);
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
            ["server", "client-a", "client-b"],
            StepDefinitions);
        ScenarioRunner runner = new(stepTimeout);
        Dictionary<string, AutomationDescription> capabilities = new(StringComparer.Ordinal);
        Dictionary<string, StateSnapshotV2> snapshots = new(StringComparer.Ordinal);
        Dictionary<string, ActionEventsResult> events = new(StringComparer.Ordinal);
        List<ScenarioActionRecordV2> actions = [];
        List<ScenarioAssertionRecordV2> assertions = [];
        Dictionary<string, string> metrics = new(StringComparer.Ordinal);
        Dictionary<string, string[]> logs = new(StringComparer.Ordinal);
        List<TerrainCellCoordinate> observedCells = [];
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
                ClientCount: 2,
                StartupTimeout: stepTimeout));
            RequiredAutomationCapabilities requiredCapabilities = new(
                SnapshotSchemaVersion: AutomationProtocol.CurrentSnapshotSchemaVersion,
                Commands: [
                    ControlCommandNames.Describe,
                    ControlCommandNames.GetSnapshotV2,
                    ControlCommandNames.GetEvents,
                    ControlCommandNames.ResetEvents,
                    ControlCommandNames.PrepareTerrainInteractionFixture,
                    ControlCommandNames.ApplyTerrainInteractionFixture,
                    ControlCommandNames.PlayerUse,
                    ControlCommandNames.PlayerPlace,
                    ControlCommandNames.PlayerDig
                ],
                Domains: [
                    AutomationCommandDomain.System,
                    AutomationCommandDomain.Fixture,
                    AutomationCommandDomain.State,
                    AutomationCommandDomain.Player,
                    AutomationCommandDomain.Terrain
                ]);
            actorPlayerIndex = await MultiplayerGameplayBootstrap.StartCreativeClusterAsync(
                cluster,
                runner,
                requiredCapabilities,
                capabilities,
                $"TerrainActions-{options.Transport}-{options.Seed}",
                options.Seed,
                stepTimeout,
                scenarioToken).ConfigureAwait(false);

            await VerifyAuthorityRejectionsAsync(
                cluster,
                runner,
                actorPlayerIndex,
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

            foreach (PlayerTerrainActionKind kind in ActionOrder) {
                await RunInteractionAsync(
                    cluster,
                    runner,
                    kind,
                    actorPlayerIndex,
                    options.ReplayPlan,
                    stepTimeout,
                    snapshots,
                    events,
                    actions,
                    assertions,
                    observedCells,
                    scenarioToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) {
            scenarioFailure = exception;
        }
        finally {
            stopwatch.Stop();
            if (cluster != null) {
                await CaptureFailureEvidenceAsync(
                    cluster,
                    observedCells,
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
                    AutomationProtocol.CurrentSnapshotSchemaVersion);
                ScenarioTopologyV2 topology = new(
                    options.Transport.ToString(),
                    "server",
                    ["client-a", "client-b"],
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
            throw new TerrainInteractionMultiplayerScenarioException(
                $"Terrain interaction multiplayer scenario failed. Artifacts: {artifactDirectory}",
                artifactDirectory,
                scenarioFailure);
        }
        if (artifactFailure != null) {
            throw new TerrainInteractionMultiplayerScenarioException(
                $"Scenario passed but artifacts could not be written to '{artifactDirectory}'.",
                artifactDirectory,
                artifactFailure);
        }
        return new TerrainInteractionMultiplayerScenarioResult(
            artifactDirectory,
            options.Transport,
            actorPlayerIndex,
            cluster?.RoomCode,
            actions.Count,
            stopwatch.Elapsed);
    }

    private static async Task RunInteractionAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        PlayerTerrainActionKind kind,
        int actorPlayerIndex,
        TerrainInteractionReplayPlan? replayPlan,
        TimeSpan stepTimeout,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events,
        List<ScenarioActionRecordV2> actions,
        List<ScenarioAssertionRecordV2> assertions,
        List<TerrainCellCoordinate> observedCells,
        CancellationToken cancellationToken) {
        string actionName = kind.ToString().ToLowerInvariant();
        TerrainCellCoordinate? replayTarget = ReplayTarget(replayPlan, kind);
        TerrainInteractionFixture fixture = await runner.RunAsync(
            $"prepare-{actionName}-fixture",
            ControlCommandNames.PrepareTerrainInteractionFixture,
            token => cluster.Server.SendAsync<PrepareTerrainInteractionFixtureCommand, TerrainInteractionFixture>(
                ControlCommandNames.PrepareTerrainInteractionFixture,
                new PrepareTerrainInteractionFixtureCommand(kind, actorPlayerIndex, replayTarget),
                cancellationToken: token),
            result => $"target={FormatCell(result.TargetCell)}; expected={FormatCell(result.ExpectedCell)}",
            cancellationToken).ConfigureAwait(false);
        EnsureFixture(fixture, kind, actorPlayerIndex, replayTarget);
        AddObservedCell(observedCells, fixture.TargetCell);
        AddObservedCell(observedCells, fixture.ExpectedCell);

        await runner.RunAsync(
            $"apply-{actionName}-fixture",
            ControlCommandNames.ApplyTerrainInteractionFixture,
            token => cluster.Clients[0].SendAsync<ApplyTerrainInteractionFixtureCommand, ControlAck>(
                ControlCommandNames.ApplyTerrainInteractionFixture,
                new ApplyTerrainInteractionFixtureCommand(fixture),
                cancellationToken: token),
            cancellationToken).ConfigureAwait(false);

        Dictionary<string, StateSnapshotV2> baseline = await runner.RunAsync(
            $"verify-{actionName}-baseline",
            ControlCommandNames.GetSnapshotV2,
            token => WaitForTerrainOnAllAsync(
                cluster,
                fixture,
                expectedFinal: false,
                stepTimeout,
                token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach ((string role, StateSnapshotV2 snapshot) in baseline) {
            snapshots[$"{role}-{actionName}-baseline"] = snapshot;
        }

        ExecutedTerrainAction executed = await runner.RunAsync(
            $"execute-{actionName}",
            CommandName(kind),
            token => ExecuteActionAsync(
                cluster.Clients[0],
                kind,
                actorPlayerIndex,
                fixture.TargetCell,
                replayPlan,
                token),
            result => $"actionId={result.Receipt.ActionId}; code={result.Receipt.Code}",
            cancellationToken).ConfigureAwait(false);
        if (!executed.Receipt.Accepted) {
            throw new InvalidOperationException(
                $"Action '{executed.Receipt.ActionId}' was rejected: {executed.Receipt.Code}.");
        }
        actions.Add(new ScenarioActionRecordV2(
            cluster.Clients[0].Role,
            executed.Command,
            executed.Payload,
            executed.Receipt));

        Dictionary<string, StateSnapshotV2> converged = await runner.RunAsync(
            $"verify-{actionName}-convergence",
            ControlCommandNames.GetSnapshotV2,
            token => WaitForTerrainOnAllAsync(
                cluster,
                fixture,
                expectedFinal: true,
                stepTimeout,
                token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach ((string role, StateSnapshotV2 snapshot) in converged) {
            snapshots[$"{role}-{actionName}-final"] = snapshot;
        }
        assertions.Add(new ScenarioAssertionRecordV2(
            $"{actionName}-server-authority-observer-convergence",
            "passed",
            MultiplayerGameplayBootstrap.AllNodes(cluster).Select(node => node.Role).ToArray(),
            $"target={FormatCell(fixture.ExpectedCell)}; value={fixture.ExpectedValue}"));

        ActionEventsResult actionEvents = await runner.RunAsync(
            $"verify-{actionName}-events",
            ControlCommandNames.GetEvents,
            token => WaitForAppliedEventAsync(
                cluster.Clients[0],
                executed.Receipt.ActionId,
                stepTimeout,
                token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        events[$"{cluster.Clients[0].Role}-{actionName}"] = actionEvents;
        assertions.Add(new ScenarioAssertionRecordV2(
            $"{actionName}-typed-action-event",
            "passed",
            [cluster.Clients[0].Role],
            $"actionId={executed.Receipt.ActionId}; phases=queued,applied"));
    }

    private static async Task VerifyAuthorityRejectionsAsync(
        MultiplayerCluster cluster,
        ScenarioRunner runner,
        int actorPlayerIndex,
        CancellationToken cancellationToken) {
        TerrainCellCoordinate arbitrary = new(0, 64, 0);
        await runner.RunAsync(
            "reject-server-originated-action",
            ControlCommandNames.PlayerUse,
            async token => {
                ControlCommandException exception = await AssertCommandRejectedAsync(() =>
                    cluster.Server.SendAsync<PlayerUseCommand, ActionReceipt>(
                        ControlCommandNames.PlayerUse,
                        new PlayerUseCommand(actorPlayerIndex, arbitrary),
                        cancellationToken: token)).ConfigureAwait(false);
                if (exception.Code != "permission_denied") {
                    throw new InvalidOperationException(
                        $"Expected permission_denied, got {exception.Code}.",
                        exception);
                }
            },
            cancellationToken).ConfigureAwait(false);

        await runner.RunAsync(
            "reject-unreachable-client-target",
            ControlCommandNames.PlayerUse,
            async token => {
                TerrainCellCoordinate unreachable = new(100000, 64, 100000);
                ControlCommandException exception = await AssertCommandRejectedAsync(() =>
                    cluster.Clients[0].SendAsync<PlayerUseCommand, ActionReceipt>(
                        ControlCommandNames.PlayerUse,
                        new PlayerUseCommand(actorPlayerIndex, unreachable),
                        cancellationToken: token)).ConfigureAwait(false);
                if (exception.Code != "target_not_reachable") {
                    throw new InvalidOperationException(
                        $"Expected target_not_reachable, got {exception.Code}.",
                        exception);
                }
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

    private static async Task<ExecutedTerrainAction> ExecuteActionAsync(
        GameNodeProcess actor,
        PlayerTerrainActionKind kind,
        int actorPlayerIndex,
        TerrainCellCoordinate target,
        TerrainInteractionReplayPlan? replayPlan,
        CancellationToken cancellationToken) {
        switch (kind) {
            case PlayerTerrainActionKind.Use: {
                PlayerUseCommand command = (replayPlan?.Use
                    ?? new PlayerUseCommand(actorPlayerIndex, target)) with {
                    PlayerIndex = actorPlayerIndex,
                    TargetCell = target
                };
                ActionReceipt receipt = await actor.SendAsync<PlayerUseCommand, ActionReceipt>(
                    ControlCommandNames.PlayerUse,
                    command,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return Executed(ControlCommandNames.PlayerUse, command, receipt);
            }
            case PlayerTerrainActionKind.Place: {
                PlayerPlaceCommand command = (replayPlan?.Place
                    ?? new PlayerPlaceCommand(actorPlayerIndex, target)) with {
                    PlayerIndex = actorPlayerIndex,
                    TargetCell = target
                };
                ActionReceipt receipt = await actor.SendAsync<PlayerPlaceCommand, ActionReceipt>(
                    ControlCommandNames.PlayerPlace,
                    command,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return Executed(ControlCommandNames.PlayerPlace, command, receipt);
            }
            case PlayerTerrainActionKind.Dig: {
                PlayerDigCommand command = (replayPlan?.Dig
                    ?? new PlayerDigCommand(actorPlayerIndex, target)) with {
                    PlayerIndex = actorPlayerIndex,
                    TargetCell = target
                };
                ActionReceipt receipt = await actor.SendAsync<PlayerDigCommand, ActionReceipt>(
                    ControlCommandNames.PlayerDig,
                    command,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return Executed(ControlCommandNames.PlayerDig, command, receipt);
            }
            default:
                throw new InvalidOperationException($"Unsupported action kind '{kind}'.");
        }
    }

    private static ExecutedTerrainAction Executed<TCommand>(
        string commandName,
        TCommand command,
        ActionReceipt receipt) =>
        new(
            commandName,
            JsonSerializer.SerializeToElement(command, ControlProtocol.JsonOptions),
            receipt);

    private static async Task<Dictionary<string, StateSnapshotV2>> WaitForTerrainOnAllAsync(
        MultiplayerCluster cluster,
        TerrainInteractionFixture fixture,
        bool expectedFinal,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        TimeSpan observationTimeout = timeout > TimeSpan.FromSeconds(2)
            ? timeout - TimeSpan.FromSeconds(1)
            : timeout;
        Task<StateSnapshotV2>[] waits = MultiplayerGameplayBootstrap.AllNodes(cluster)
            .Select(node => WaitForTerrainAsync(
                node,
                fixture,
                expectedFinal,
                observationTimeout,
                cancellationToken))
            .ToArray();
        StateSnapshotV2[] captured = await Task.WhenAll(waits).ConfigureAwait(false);
        return MultiplayerGameplayBootstrap.AllNodes(cluster)
            .Select((node, index) => (node.Role, Snapshot: captured[index]))
            .ToDictionary(item => item.Role, item => item.Snapshot, StringComparer.Ordinal);
    }

    private static async Task<StateSnapshotV2> WaitForTerrainAsync(
        GameNodeProcess node,
        TerrainInteractionFixture fixture,
        bool expectedFinal,
        TimeSpan timeout,
        CancellationToken cancellationToken) {
        TerrainCellCoordinate[] coordinates = [fixture.TargetCell, fixture.ExpectedCell];
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        StateSnapshotV2? last = null;
        int expectedValue = expectedFinal ? fixture.ExpectedValue : fixture.InitialValue;
        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            last = await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                ControlCommandNames.GetSnapshotV2,
                new GetSnapshotV2Command(
                    [SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.Terrain],
                    PlayerIndexes: [fixture.PlayerIndex],
                    TerrainCells: coordinates),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            TerrainCellSnapshotV2? target = FindCell(last, fixture.TargetCell);
            TerrainCellSnapshotV2? expected = FindCell(last, fixture.ExpectedCell);
            PlayerSnapshotV2? actor = last.Players.SingleOrDefault(player =>
                player.PlayerIndex == fixture.PlayerIndex);
            bool cellsLoaded = target?.IsLoaded == true && expected?.IsLoaded == true;
            bool targetMatches = fixture.TargetCell == fixture.ExpectedCell
                ? expected?.Value == expectedValue
                : target?.Value == fixture.TargetValue;
            bool actorPositionMatches = actor?.Body is { } body
                && PositionMatches(body.Position, fixture.PlayerPosition);
            if (cellsLoaded
                && targetMatches
                && expected?.Value == expectedValue
                && actorPositionMatches) {
                return last;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }
        string actual = last == null
            ? "missing snapshot"
            : string.Join(", ", last.TerrainCells.Select(cell =>
                $"{cell.X},{cell.Y},{cell.Z}={cell.Value}(loaded={cell.IsLoaded})"));
        Vector3State? actualPosition = last?.Players
            .SingleOrDefault(player => player.PlayerIndex == fixture.PlayerIndex)
            ?.Body?.Position;
        throw new TimeoutException(
            $"Node '{node.Role}' did not converge {fixture.Kind} fixture. "
            + $"ExpectedTarget={FormatCell(fixture.TargetCell)}={fixture.TargetValue}; "
            + $"ExpectedCell={FormatCell(fixture.ExpectedCell)}={expectedValue}; "
            + $"ActualTerrain={actual}; "
            + $"ExpectedPosition={FormatVector(fixture.PlayerPosition)}; "
            + $"ActualPosition={FormatVector(actualPosition)}.");
    }

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
            $"Action '{actionId}' did not publish queued and applied events. Phases={phases}.");
    }

    private static async Task CaptureFailureEvidenceAsync(
        MultiplayerCluster cluster,
        IReadOnlyCollection<TerrainCellCoordinate> observedCells,
        Dictionary<string, StateSnapshotV2> snapshots,
        Dictionary<string, ActionEventsResult> events) {
        foreach (GameNodeProcess node in MultiplayerGameplayBootstrap.AllNodes(cluster)) {
            if (!node.IsRunning) {
                continue;
            }
            try {
                snapshots[$"{node.Role}-final"] = await node.SendAsync<GetSnapshotV2Command, StateSnapshotV2>(
                    ControlCommandNames.GetSnapshotV2,
                    new GetSnapshotV2Command(
                        [
                            SnapshotDomain.Network,
                            SnapshotDomain.Players,
                            SnapshotDomain.Terrain,
                            SnapshotDomain.Events
                        ],
                        TerrainCells: observedCells.ToArray(),
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

    private static TerrainCellSnapshotV2? FindCell(
        StateSnapshotV2 snapshot,
        TerrainCellCoordinate coordinate) =>
        snapshot.TerrainCells.SingleOrDefault(cell =>
            cell.X == coordinate.X && cell.Y == coordinate.Y && cell.Z == coordinate.Z);

    private static bool PositionMatches(Vector3State actual, Vector3State expected) =>
        Math.Abs(actual.X - expected.X) <= 0.25
        && Math.Abs(actual.Y - expected.Y) <= 0.25
        && Math.Abs(actual.Z - expected.Z) <= 0.25;

    private static void EnsureFixture(
        TerrainInteractionFixture fixture,
        PlayerTerrainActionKind kind,
        int actorPlayerIndex,
        TerrainCellCoordinate? replayTarget) {
        if (fixture.Kind != kind || fixture.PlayerIndex != actorPlayerIndex) {
            throw new InvalidOperationException(
                $"Fixture identity mismatch. Expected={kind}/{actorPlayerIndex}, "
                + $"Actual={fixture.Kind}/{fixture.PlayerIndex}.");
        }
        if (replayTarget != null && fixture.TargetCell != replayTarget) {
            throw new InvalidOperationException(
                $"Replay target mismatch. Expected={replayTarget}, Actual={fixture.TargetCell}.");
        }
        if (fixture.InitialValue == fixture.ExpectedValue) {
            throw new InvalidOperationException(
                $"Fixture {kind} does not describe a state transition at {fixture.ExpectedCell}.");
        }
    }

    private static void ValidateReplayPlan(TerrainInteractionReplayPlan? replayPlan) {
        if (replayPlan == null) {
            return;
        }
        if (replayPlan.Dig.HoldFrames is < 1 or > 1200) {
            throw new InvalidDataException("Replay dig holdFrames must be between 1 and 1200.");
        }
    }

    private static TerrainCellCoordinate? ReplayTarget(
        TerrainInteractionReplayPlan? replayPlan,
        PlayerTerrainActionKind kind) =>
        kind switch {
            PlayerTerrainActionKind.Use => replayPlan?.Use.TargetCell,
            PlayerTerrainActionKind.Place => replayPlan?.Place.TargetCell,
            PlayerTerrainActionKind.Dig => replayPlan?.Dig.TargetCell,
            _ => null
        };

    private static string CommandName(PlayerTerrainActionKind kind) =>
        kind switch {
            PlayerTerrainActionKind.Use => ControlCommandNames.PlayerUse,
            PlayerTerrainActionKind.Place => ControlCommandNames.PlayerPlace,
            PlayerTerrainActionKind.Dig => ControlCommandNames.PlayerDig,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static TCommand DeserializeAction<TCommand>(ScenarioActionRecordV2 record) =>
        record.Payload.Deserialize<TCommand>(ControlProtocol.JsonOptions)
        ?? throw new InvalidDataException(
            $"Replay action '{record.Receipt.ActionId}' has an invalid {typeof(TCommand).Name} payload.");

    private static void AddObservedCell(
        ICollection<TerrainCellCoordinate> cells,
        TerrainCellCoordinate coordinate) {
        if (!cells.Contains(coordinate)) {
            cells.Add(coordinate);
        }
    }

    private static string FormatCell(TerrainCellCoordinate cell) =>
        $"{cell.X},{cell.Y},{cell.Z}";

    private static string FormatVector(Vector3State? value) =>
        value == null
            ? "missing"
            : FormattableString.Invariant($"{value.X:R},{value.Y:R},{value.Z:R}");

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

    private sealed record ExecutedTerrainAction(
        string Command,
        JsonElement Payload,
        ActionReceipt Receipt);
}
