using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class EnvironmentMechanicsProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void AutoTestV7EnvironmentAndMechanicsCommandsRoundTrip() {
        EnvironmentRequestChangeCommand environment = new(
            3,
            EnvironmentChangeKind.TimeOfDay,
            TimeOfDayOffset: 0.375,
            ForgeAuthoritativeState: true,
            ExpectRejected: true);
        PlayerSleepCommand sleep = new(
            3,
            Sleep: true,
            AllowManualWakeup: false,
            TargetEntityId: 41,
            ExpectRejected: true);
        SetEnvironmentFixtureCommand survivalFixture = new(
            TimeOfDayOffset: 0.125,
            TimeOfYear: 0.375f,
            CreativeMode: false);

        ControlRequest environmentRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.EnvironmentRequestChange,
            environment);
        ControlRequest sleepRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PlayerSleep,
            sleep);
        ControlRequest fixtureRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.SetEnvironmentFixture,
            survivalFixture);

        Assert.Equal(9, AutomationProtocol.CurrentApiVersion);
        Assert.Equal(environment, environmentRequest.DeserializePayload<EnvironmentRequestChangeCommand>());
        Assert.Equal(sleep, sleepRequest.DeserializePayload<PlayerSleepCommand>());
        Assert.Equal(survivalFixture,
            fixtureRequest.DeserializePayload<SetEnvironmentFixtureCommand>());
        Assert.Contains(AutomationCommandDomain.Environment, Enum.GetValues<AutomationCommandDomain>());
        Assert.Contains(AutomationCommandDomain.Mechanics, Enum.GetValues<AutomationCommandDomain>());
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EnvironmentAndMechanicsSnapshotProjectionIsCanonical() {
        EnvironmentSnapshotV2 environment = new(
            1.234567,
            0.375004,
            0.500004f,
            10.123456,
            20.765432,
            1.234567f,
            0.765432f,
            true,
            30.123456,
            40.765432,
            2.345678f,
            0.456789f,
            true,
            5.123456,
            new Vector3State(1.234567, 2.345678, 3.456789),
            3,
            5,
            7,
            "Creative");
        MechanicsStateSnapshotV2 first = new(
            41,
            true,
            true,
            3,
            true,
            false,
            true,
            2.345678,
            4,
            true,
            true,
            119.123456,
            6,
            8);
        MechanicsStateSnapshotV2 second = first with {
            EntityId = 40,
            PlayerIndex = 2,
            FireDuration = 118.765432
        };
        StateSnapshotV2 snapshot = Snapshot() with {
            Environment = environment,
            MechanicsStates = [first, second]
        };

        StateSnapshotV2 normalized = SnapshotV2Canonicalizer.Normalize(snapshot, decimalPlaces: 5);

        Assert.Equal(0.375, normalized.Environment!.TimeOfDayOffset);
        Assert.Equal("Creative", normalized.Environment.GameMode);
        Assert.Equal(5.12346, normalized.Environment.LastLightningStrikeTime);
        Assert.Equal(new Vector3State(1.23457, 2.34568, 3.45679),
            normalized.Environment.LightningStrikePosition);
        MechanicsStateSnapshotV2[] mechanics = Assert.IsType<MechanicsStateSnapshotV2[]>(
            normalized.MechanicsStates);
        Assert.Equal([40, 41], mechanics.Select(state => state.EntityId));
        Assert.Equal(119.12346, mechanics[1].FireDuration);
        Assert.Equal(2.34568, mechanics[1].SleepStartTime);

        StateSnapshotV2 reordered = snapshot with {
            MechanicsStates = [second, first]
        };
        Assert.Equal(
            SnapshotV2Canonicalizer.ComputeSha256(snapshot),
            SnapshotV2Canonicalizer.ComputeSha256(reordered));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EnvironmentMechanicsReplayParserRequiresStrictCrossDomainSequence() {
        ScenarioActionRecordV2[] records = [
            ClientAction(
                "time",
                ControlCommandNames.EnvironmentRequestChange,
                new EnvironmentRequestChangeCommand(
                    1,
                    EnvironmentChangeKind.TimeOfDay,
                    TimeOfDayOffset: 0.375)),
            ClientAction(
                "precipitation",
                ControlCommandNames.EnvironmentRequestChange,
                new EnvironmentRequestChangeCommand(1, EnvironmentChangeKind.PrecipitationStart)),
            ClientAction(
                "fog",
                ControlCommandNames.EnvironmentRequestChange,
                new EnvironmentRequestChangeCommand(1, EnvironmentChangeKind.FogStart)),
            ClientAction(
                "lightning",
                ControlCommandNames.EnvironmentRequestChange,
                new EnvironmentRequestChangeCommand(1, EnvironmentChangeKind.Lightning)),
            ClientAction(
                "sleep",
                ControlCommandNames.PlayerSleep,
                new PlayerSleepCommand(1, Sleep: true)),
            ServerAction(
                "environment-burst",
                ControlCommandNames.RunEnvironmentRevisionBurst,
                new RunEnvironmentRevisionBurstCommand(FinalPrecipitationStarted: true)),
            ServerAction(
                "mechanics-burst",
                ControlCommandNames.RunMechanicsRevisionBurst,
                new RunMechanicsRevisionBurstCommand(31, 180f, 120f)),
            ClientAction(
                "wake",
                ControlCommandNames.PlayerSleep,
                new PlayerSleepCommand(1, Sleep: false))
        ];

        EnvironmentMechanicsReplayPlan plan =
            EnvironmentMechanicsMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(0.375, plan.Time.TimeOfDayOffset);
        Assert.Equal(EnvironmentChangeKind.Lightning, plan.Lightning.Change);
        Assert.True(plan.Sleep.Sleep);
        Assert.Equal(120f, plan.MechanicsRevisionBurst.FinalDuration);
        Assert.False(plan.Wake.Sleep);
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentMechanicsMultiplayerScenario.ParseReplayActions([
                records[0] with { Role = "server" },
                .. records[1..]
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentMechanicsMultiplayerScenario.ParseReplayActions([
                .. records[..6],
                records[6] with {
                    Payload = JsonSerializer.SerializeToElement(
                        new RunMechanicsRevisionBurstCommand(
                            31,
                            SendStaleSnapshot: false),
                        ControlProtocol.JsonOptions)
                },
                records[7]
            ]));
    }

    private static StateSnapshotV2 Snapshot() => new(
        AutomationProtocol.CurrentSnapshotSchemaVersion,
        "capture",
        12,
        1.234567,
        new NodeSnapshotV2("client-a", "GameScreen", true, true, "network"),
        new NetworkSnapshotV2("client", "Playing", 1, 0, "none", 0, 0, 0, 0),
        [],
        [],
        [],
        new Dictionary<string, string>());

    private static ScenarioActionRecordV2 ClientAction<TCommand>(
        string actionId,
        string command,
        TCommand payload) =>
        Action("client-a", actionId, command, payload, 1);

    private static ScenarioActionRecordV2 ServerAction<TCommand>(
        string actionId,
        string command,
        TCommand payload) =>
        Action("server", actionId, command, payload, -1);

    private static ScenarioActionRecordV2 Action<TCommand>(
        string role,
        string actionId,
        string command,
        TCommand payload,
        int actorPlayerIndex) => new(
        role,
        command,
        JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
        new ActionReceipt(actionId, true, 1, actorPlayerIndex, "applied"));
}
