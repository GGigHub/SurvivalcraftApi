using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class AutomationProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void RouterDispatchesTypedCommandAndPublishesMetadata() {
        TypedCommandRouter router = new();
        AutomationCommandDescriptor descriptor = Descriptor("test.echo");
        router.Register<PingCommand, PingResult>(
            descriptor,
            (context, command) => new PingResult(
                command.Message ?? string.Empty,
                context.NodeRole,
                Environment.ProcessId,
                ControlProtocol.CurrentVersion,
                DateTimeOffset.UnixEpoch));
        ControlRequest request = ControlProtocol.CreateRequest(
            "test.echo",
            new PingCommand("hello"),
            "action-1");

        ControlResponse response = router.Dispatch(request, 17, "unit");
        PingResult result = response.DeserializeResult<PingResult>();

        Assert.Equal("hello", result.Message);
        Assert.Equal("unit", result.Role);
        Assert.Equal(17, response.Sequence);
        Assert.Equal(descriptor, Assert.Single(router.Commands));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void RouterRejectsDuplicateCommandNamesAtRegistration() {
        TypedCommandRouter router = new();
        router.Register<EmptyCommand, ControlAck>(
            Descriptor("test.duplicate"),
            (_, _) => new ControlAck(true));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            router.Register<EmptyCommand, ControlAck>(
                Descriptor("test.duplicate"),
                (_, _) => new ControlAck(true)));

        Assert.Contains("already registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void RouterReturnsStableCommandErrorCode() {
        TypedCommandRouter router = new();
        router.Register<EmptyCommand, ControlAck>(
            Descriptor("test.reject"),
            (_, _) => throw new AutomationCommandException(
                "permission_denied",
                "Rejected for test."));
        ControlRequest request = ControlProtocol.CreateRequest(
            "test.reject",
            new EmptyCommand());

        ControlResponse response = router.Dispatch(request, 2, "unit");
        ControlCommandException exception = Assert.Throws<ControlCommandException>(
            response.DeserializeResult<ControlAck>);

        Assert.Equal("permission_denied", exception.Code);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CapabilityValidationReportsAllMissingRequirements() {
        AutomationDescription description = new(
            ControlProtocol.CurrentVersion,
            AutomationProtocol.CurrentApiVersion,
            [1],
            [Descriptor(ControlCommandNames.Ping)],
            [AutomationCommandDomain.System.ToString()],
            new AutomationLimits(
                ControlProtocol.MaximumFrameBytes,
                AutomationProtocol.MaximumSnapshotEntities,
                AutomationProtocol.MaximumSnapshotTerrainCells,
                AutomationProtocol.MaximumSnapshotInventorySlots,
                AutomationProtocol.MaximumSnapshotLogicElements,
                AutomationProtocol.MaximumEventQueryCount,
                AutomationProtocol.DefaultEventJournalCapacity),
            "test",
            LegacyExecutionEnabled: true);

        ControlProtocolException exception = Assert.Throws<ControlProtocolException>(() =>
            AutomationCapabilityValidator.EnsureSupported(
                description,
                new RequiredAutomationCapabilities(
                    SnapshotSchemaVersion: 2,
                    Commands: [ControlCommandNames.GetSnapshotV2],
                    Domains: [AutomationCommandDomain.Player])));

        Assert.Equal("unsupported_capability", exception.Code);
        Assert.Contains("snapshotSchema=2", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ControlCommandNames.GetSnapshotV2, exception.Message, StringComparison.Ordinal);
        Assert.Contains("domain=Player", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EventJournalIsBoundedIncrementalAndResettable() {
        ActionEventJournal journal = new(capacity: 2);
        journal.Record(
            1,
            "a1",
            "client-a",
            AutomationCommandDomain.Player,
            "creativeFly",
            ActionEventPhase.Queued,
            timestamp: DateTimeOffset.UnixEpoch);
        journal.Record(
            2,
            "a1",
            "client-a",
            AutomationCommandDomain.Player,
            "creativeFly",
            ActionEventPhase.Applied,
            timestamp: DateTimeOffset.UnixEpoch);
        journal.Record(
            3,
            "a2",
            "client-a",
            AutomationCommandDomain.Player,
            "creativeFly",
            ActionEventPhase.Rejected,
            timestamp: DateTimeOffset.UnixEpoch);

        ActionEventsResult result = journal.Query(new GetEventsCommand(AfterSequence: 0, Limit: 2));

        Assert.Equal([2L, 3L], result.Events.Select(item => item.Sequence));
        Assert.Equal(1, result.DroppedEvents);
        Assert.Equal(2, result.OldestAvailableSequence);
        Assert.Equal(3, result.LatestSequence);
        Assert.False(result.Truncated);

        ActionEventsResult truncated = journal.Query(new GetEventsCommand(AfterSequence: 0, Limit: 1));
        Assert.Single(truncated.Events);
        Assert.False(truncated.Truncated);
        Assert.Equal(3, journal.Reset().LatestSequence);
        Assert.Empty(journal.Query(new GetEventsCommand()).Events);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EventJournalRejectsQueriesBeyondPublishedLimit() {
        ActionEventJournal journal = new();

        AutomationCommandException exception = Assert.Throws<AutomationCommandException>(() =>
            journal.Query(new GetEventsCommand(
                Limit: AutomationProtocol.MaximumEventQueryCount + 1)));

        Assert.Equal("limit_exceeded", exception.Code);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainInteractionActionsRoundTripThroughTypedWireContract() {
        TerrainCellCoordinate target = new(12, 65, 24);
        ControlRequest placeRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PlayerPlace,
            new PlayerPlaceCommand(1, target));
        ControlRequest digRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PlayerDig,
            new PlayerDigCommand(1, target, HoldFrames: 300));
        ControlRequest fixtureRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PrepareTerrainInteractionFixture,
            new PrepareTerrainInteractionFixtureCommand(
                PlayerTerrainActionKind.Use,
                1,
                target));

        Assert.Equal(target, placeRequest.DeserializePayload<PlayerPlaceCommand>().TargetCell);
        Assert.Equal(300, digRequest.DeserializePayload<PlayerDigCommand>().HoldFrames);
        PrepareTerrainInteractionFixtureCommand fixture =
            fixtureRequest.DeserializePayload<PrepareTerrainInteractionFixtureCommand>();
        Assert.Equal(PlayerTerrainActionKind.Use, fixture.Kind);
        Assert.Equal(target, fixture.TargetCell);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PlayerMotionActionsRoundTripThroughTypedWireContract() {
        Vector3State start = new(12.5, 65.01, 24.5);
        Vector3State move = new(0.25, 0, 0.75);
        Vector2State look = new(0.5, -0.25);
        ControlRequest fixtureRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PreparePlayerMotionFixture,
            new PreparePlayerMotionFixtureCommand(7, start));
        ControlRequest moveRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PlayerMove,
            new PlayerMoveCommand(7, move, HoldFrames: 60));
        ControlRequest lookRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PlayerLook,
            new PlayerLookCommand(7, look, HoldFrames: 12));
        ControlRequest crouchRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PlayerSetCrouch,
            new PlayerSetCrouchCommand(7, true));

        Assert.Equal(
            start,
            fixtureRequest.DeserializePayload<PreparePlayerMotionFixtureCommand>().StartPosition);
        PlayerMoveCommand decodedMove = moveRequest.DeserializePayload<PlayerMoveCommand>();
        Assert.Equal(move, decodedMove.Input);
        Assert.Equal(60, decodedMove.HoldFrames);
        PlayerLookCommand decodedLook = lookRequest.DeserializePayload<PlayerLookCommand>();
        Assert.Equal(look, decodedLook.Input);
        Assert.Equal(12, decodedLook.HoldFrames);
        Assert.True(crouchRequest.DeserializePayload<PlayerSetCrouchCommand>().Enabled);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EntityLifecycleActionsRoundTripThroughTypedWireContract() {
        Vector3State impulse = new(1.5, 0, 0.5);
        ControlRequest fixtureRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PrepareEntityLifecycleFixture,
            new PrepareEntityLifecycleFixtureCommand(7));
        ControlRequest impulseRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.ApplyEntityImpulse,
            new ApplyEntityImpulseCommand("survivor", impulse, ActiveFrames: 12));
        ControlRequest burstRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.RunEntityLifecycleBurst,
            new RunEntityLifecycleBurstCommand());

        PrepareEntityLifecycleFixtureCommand fixture =
            fixtureRequest.DeserializePayload<PrepareEntityLifecycleFixtureCommand>();
        ApplyEntityImpulseCommand decodedImpulse =
            impulseRequest.DeserializePayload<ApplyEntityImpulseCommand>();
        RunEntityLifecycleBurstCommand burst =
            burstRequest.DeserializePayload<RunEntityLifecycleBurstCommand>();

        Assert.Equal(7, fixture.ActorPlayerIndex);
        Assert.Equal("Cow", fixture.TemplateName);
        Assert.Equal("survivor", decodedImpulse.Alias);
        Assert.Equal(impulse, decodedImpulse.Impulse);
        Assert.Equal(12, decodedImpulse.ActiveFrames);
        Assert.True(burst.SendStaleLoads);
    }

    private static AutomationCommandDescriptor Descriptor(string name) =>
        new(
            name,
            AutomationCommandDomain.System,
            AutomationCommandStability.Stable,
            MutatesState: false,
            AllowedRoles: ["*"]);
}
