using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class SurvivalStateProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void AutoTestV14SurvivalStateCommandsRoundTripCompleteFixtureIdentity() {
        SurvivalStateFixture fixture = Fixture();
        SetCreatureBehaviorStateCommand behavior = new(fixture.Behaviors, true, 0.875f);
        SetPlayerSurvivalStateCommand player = new(
            fixture,
            ExperienceCount: 55,
            BlocksDugIncrement: 9,
            Food: 0.81f,
            Stamina: 0.72f,
            Sleep: 0.63f,
            Wetness: 0.24f,
            Temperature: 11.25f);

        ControlRequest behaviorRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.SetCreatureBehaviorState,
            behavior);
        ControlRequest playerRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.SetPlayerSurvivalState,
            player);
        ControlRequest quiesceRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.QuiesceSurvivalStateFixture,
            new QuiesceSurvivalStateFixtureCommand(fixture.Behaviors));

        Assert.Equal(16, AutomationProtocol.CurrentApiVersion);
        Assert.Equal(5, AutomationProtocol.CurrentSnapshotSchemaVersion);
        Assert.Equal(
            behavior,
            behaviorRequest.DeserializePayload<SetCreatureBehaviorStateCommand>());
        Assert.Equal(
            player,
            playerRequest.DeserializePayload<SetPlayerSurvivalStateCommand>());
        Assert.Equal(
            fixture.Behaviors,
            quiesceRequest.DeserializePayload<QuiesceSurvivalStateFixtureCommand>().Fixture);
        Assert.Equal(Enumerable.Range(101, 9), fixture.Behaviors.EntityIds);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SurvivalSnapshotCanonicalizationSortsAndRoundsEveryNestedDomain() {
        SurvivalPlayerStateSnapshotV2 player = new(
            PlayerIndex: 2,
            EntityId: 42,
            Level: 4.123456,
            SkinName: "NetworkSkin.scskin",
            HasSkinResource: true,
            ClothingInventoryId: 42,
            ClothingSlots: [
                new ClothingSlotStateSnapshotV2(3, [103]),
                new ClothingSlotStateSnapshotV2(0, [100, 101])
            ],
            Food: 0.8123456,
            Stamina: 0.7123456,
            Sleep: 0.6123456,
            Wetness: 0.2123456,
            Temperature: 11.234567,
            FluOnset: 1.123456,
            FluDuration: 2.123456,
            CoughDuration: 3.123456,
            SneezeDuration: 4.123456,
            HasFlu: true,
            SicknessDuration: 5.123456,
            IsSick: true,
            IsPuking: false,
            BlocksDug: 9,
            FoodItemsEaten: 8,
            TimesWasSick: 7,
            TimesHadFlu: 6,
            TimesPuked: 5,
            HighestLevel: 4.123456,
            ClothingRevision: 11,
            ProgressionRevision: 12,
            PlayerStatsRevision: 13,
            VitalStatsRevision: 14,
            FluRevision: 15,
            SicknessRevision: 16);
        CreatureBehaviorStateSnapshotV2 later = Creature(102, 0.876543);
        CreatureBehaviorStateSnapshotV2 earlier = Creature(101, 0.765432);
        StateSnapshotV2 snapshot = Snapshot() with {
            SurvivalState = new SurvivalStateSnapshotV2([player], [later, earlier])
        };

        StateSnapshotV2 normalized = SnapshotV2Canonicalizer.Normalize(snapshot);
        SurvivalStateSnapshotV2 state = Assert.IsType<SurvivalStateSnapshotV2>(
            normalized.SurvivalState);

        Assert.Equal([0, 3], state.Players[0].ClothingSlots.Select(slot => slot.SlotIndex));
        Assert.Equal(4.12346, state.Players[0].Level);
        Assert.Equal(0.81235, state.Players[0].Food);
        Assert.Equal([101, 102], state.Creatures.Select(creature => creature.EntityId));
        Assert.Equal(0.76543, state.Creatures[0].HeadShakeOrder);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SurvivalReplayParserRequiresExactEightActionAuthorityContract() {
        SurvivalStateFixture fixture = Fixture();
        ScenarioActionRecordV2[] records = [
            Action("server", ControlCommandNames.SetCreatureBehaviorState,
                new SetCreatureBehaviorStateCommand(fixture.Behaviors)),
            Action("server", ControlCommandNames.SetPlayerSurvivalState,
                new SetPlayerSurvivalStateCommand(fixture)),
            Action("client-a", ControlCommandNames.RequestSkinTransfer,
                new RequestSkinTransferCommand(1, fixture.SkinName)),
            Action("server", ControlCommandNames.TriggerCreatureSound,
                new TriggerCreatureSoundCommand(fixture.Behaviors.SoundEntityId)),
            Action("client-a", ControlCommandNames.TriggerCreatureSound,
                new TriggerCreatureSoundCommand(fixture.Behaviors.SoundEntityId, CreatureSoundKind.Pain)),
            Action("client-a", ControlCommandNames.ForgeSurvivalState,
                new ForgeSurvivalStateCommand(fixture)),
            Action("server", ControlCommandNames.RunSurvivalStateRevisionBurst,
                new RunSurvivalStateRevisionBurstCommand(fixture)),
            Action("client-c", ControlCommandNames.RequestSkinTransfer,
                new RequestSkinTransferCommand(3, fixture.SkinName))
        ];

        SurvivalStateReplayPlan plan =
            SurvivalStateMultiplayerScenario.ParseReplayActions(records);

        Assert.True(plan.BehaviorState.Enabled);
        Assert.Equal(fixture, plan.PlayerState.Fixture);
        Assert.True(plan.RevisionBurst.SendStaleStates);
        Assert.Equal(3, plan.LateJoinSkinRequest.PlayerIndex);
        Assert.Throws<InvalidDataException>(() =>
            SurvivalStateMultiplayerScenario.ParseReplayActions(records[..7]));
        Assert.Throws<InvalidDataException>(() =>
            SurvivalStateMultiplayerScenario.ParseReplayActions([
                records[0] with { Role = "client-a" },
                .. records[1..]
            ]));
    }

    private static SurvivalStateFixture Fixture() => new(
        PlayerIndex: 1,
        PlayerEntityId: 42,
        ClothingInventoryId: 42,
        ClothingSlotIndex: 0,
        ClothingValue: 203,
        SkinName: "NetworkSkin.scskin",
        Behaviors: new CreatureBehaviorFixture(101, 102, 103, 104, 105, 106, 107, 108, 109));

    private static CreatureBehaviorStateSnapshotV2 Creature(
        int entityId,
        double headShakeOrder) => new(
            EntityId: entityId,
            TemplateName: "Cow",
            ChaseAttack: true,
            DigInMud: true,
            EatPickable: true,
            FishOutOfWater: true,
            LookAround: true,
            RandomFeed: true,
            RandomPeck: true,
            HeadShakeOrder: headShakeOrder,
            LastSoundTime: 1.234567,
            LastCoughSoundTime: 2.234567,
            LastPukeSoundTime: 3.234567,
            ChaseRevision: 1,
            DigInMudRevision: 2,
            EatPickableRevision: 3,
            FishOutOfWaterRevision: 4,
            LookAroundRevision: 5,
            RandomFeedRevision: 6,
            RandomPeckRevision: 7,
            StubbornSteedRevision: 8);

    private static StateSnapshotV2 Snapshot() => new(
        AutomationProtocol.CurrentSnapshotSchemaVersion,
        "capture",
        1,
        0,
        new NodeSnapshotV2("server", "Game", true, true, "World"),
        null,
        [],
        [],
        [],
        new Dictionary<string, string>());

    private static ScenarioActionRecordV2 Action<TCommand>(
        string role,
        string command,
        TCommand payload) => new(
            role,
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            new ActionReceipt(Guid.NewGuid().ToString("N"), true, 1, role == "server" ? -1 : 1, "applied"));
}
