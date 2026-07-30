using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class MountProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void AutoTestV7MountCommandsRoundTripWithoutLosingSecurityFields() {
        PlayerMountCommand action = new(
            3,
            52,
            Mount: true,
            TargetRiderEntityId: 41,
            ForgeAuthoritativeState: true,
            ExpectRejected: true);
        RunMountRevisionBurstCommand revisionBurst = new(41, 52, false, true);
        RunMountLifecycleBurstCommand lifecycleBurst = new("primary", 41, true, true);

        ControlRequest actionRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.PlayerMount,
            action);
        ControlRequest revisionRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.RunMountRevisionBurst,
            revisionBurst);
        ControlRequest lifecycleRequest = ControlProtocol.CreateRequest(
            ControlCommandNames.RunMountLifecycleBurst,
            lifecycleBurst);

        Assert.Equal(10, AutomationProtocol.CurrentApiVersion);
        Assert.Equal(action, actionRequest.DeserializePayload<PlayerMountCommand>());
        Assert.Equal(revisionBurst,
            revisionRequest.DeserializePayload<RunMountRevisionBurstCommand>());
        Assert.Equal(lifecycleBurst,
            lifecycleRequest.DeserializePayload<RunMountLifecycleBurstCommand>());
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MountSnapshotProjectionPreservesBothSidesAndLifecycleStamp() {
        MechanicsStateSnapshotV2 rider = RiderState();
        MechanicsStateSnapshotV2 mount = MountState();
        StateSnapshotV2 snapshot = Snapshot() with {
            MechanicsStates = [mount, rider]
        };

        StateSnapshotV2 normalized = SnapshotV2Canonicalizer.Normalize(snapshot);
        MechanicsStateSnapshotV2[] states = Assert.IsType<MechanicsStateSnapshotV2[]>(
            normalized.MechanicsStates);

        Assert.Equal([41, 52], states.Select(state => state.EntityId));
        Assert.Equal(52, states[0].MountEntityId);
        Assert.Equal(9, states[0].MountEntityLifecycleRevision);
        Assert.Equal(11, states[0].MountRevision);
        Assert.Equal(41, states[1].RiderEntityId);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MechanicsComparerDetectsBothRelationSidesAndRevisionGenerations() {
        StateSnapshotV2 expected = Snapshot() with {
            MechanicsStates = [RiderState(), MountState()]
        };
        StateSnapshotV2 actual = Snapshot() with {
            MechanicsStates = [
                RiderState() with {
                    MountEntityId = 53,
                    MountRevision = 12,
                    MountEntityLifecycleRevision = 10
                },
                MountState() with { RiderEntityId = 42 }
            ]
        };

        SnapshotV2Diff diff = SnapshotV2Comparer.CompareMechanicsStates(expected, actual);

        Assert.False(diff.Equal);
        Assert.Contains(diff.Differences, item =>
            item.Path == "$.mechanicsStates[entityId=41].mountEntityId");
        Assert.Contains(diff.Differences, item =>
            item.Path == "$.mechanicsStates[entityId=41].mountRevision");
        Assert.Contains(diff.Differences, item =>
            item.Path == "$.mechanicsStates[entityId=41].mountEntityLifecycleRevision");
        Assert.Contains(diff.Differences, item =>
            item.Path == "$.mechanicsStates[entityId=52].riderEntityId");

        StateSnapshotV2 revisionOnly = expected with {
            MechanicsStates = [
                RiderState() with {
                    MountRevision = 99,
                    MountEntityLifecycleRevision = 100
                },
                MountState()
            ]
        };
        SnapshotV2Diff semanticDiff = SnapshotV2Comparer.CompareMechanicsStates(
            expected,
            revisionOnly,
            new SnapshotV2MechanicsComparisonOptions(
                CompareRevisions: false,
                CompareLifecycleRevisions: false));
        Assert.True(semanticDiff.Equal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MountReplayParserRequiresExactSevenActionSecurityContract() {
        ScenarioActionRecordV2[] records = [
            Action("client-a", "mount", ControlCommandNames.PlayerMount,
                new PlayerMountCommand(3, 52, Mount: true)),
            Action("client-a", "dismount", ControlCommandNames.PlayerMount,
                new PlayerMountCommand(3, 52, Mount: false)),
            Action("client-a", "fault-mount", ControlCommandNames.PlayerMount,
                new PlayerMountCommand(3, 52, Mount: true)),
            Action("server", "revision", ControlCommandNames.RunMountRevisionBurst,
                new RunMountRevisionBurstCommand(41, 52, FinalMounted: false, SendStaleState: true)),
            Action("server", "lifecycle", ControlCommandNames.RunMountLifecycleBurst,
                new RunMountLifecycleBurstCommand("primary", 41, ReuseEntityId: true, SendStaleState: true)),
            Action("client-a", "remount", ControlCommandNames.PlayerMount,
                new PlayerMountCommand(3, 52, Mount: true)),
            Action("client-a", "reconnect-mount", ControlCommandNames.PlayerMount,
                new PlayerMountCommand(3, 52, Mount: true))
        ];

        MountReplayPlan plan = MountMultiplayerScenario.ParseReplayActions(records);

        Assert.True(plan.Mount.Mount);
        Assert.False(plan.Dismount.Mount);
        Assert.True(plan.RevisionBurst.SendStaleState);
        Assert.True(plan.LifecycleBurst.ReuseEntityId);
        Assert.True(plan.LifecycleBurst.SendStaleState);
        Assert.Throws<InvalidDataException>(() =>
            MountMultiplayerScenario.ParseReplayActions([
                records[0] with { Role = "server" },
                .. records[1..]
            ]));
        Assert.Throws<InvalidDataException>(() =>
            MountMultiplayerScenario.ParseReplayActions([
                .. records[..4],
                records[4] with {
                    Payload = JsonSerializer.SerializeToElement(
                        new RunMountLifecycleBurstCommand(
                            "primary",
                            41,
                            ReuseEntityId: true,
                            SendStaleState: false),
                        ControlProtocol.JsonOptions)
                },
                .. records[5..]
            ]));
    }

    private static ScenarioActionRecordV2 Action<TCommand>(
        string role,
        string actionId,
        string command,
        TCommand payload) =>
        new(
            role,
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            new ActionReceipt(actionId, true, 1, role == "server" ? -1 : 3, "applied"));

    private static MechanicsStateSnapshotV2 RiderState() => new(
        41,
        true,
        true,
        3,
        false,
        false,
        false,
        null,
        -1,
        false,
        false,
        0,
        -1,
        7,
        HasRider: true,
        MountEntityId: 52,
        MountAnimating: false,
        IsDismounting: false,
        MountRevision: 11,
        MountEntityLifecycleRevision: 9);

    private static MechanicsStateSnapshotV2 MountState() => new(
        52,
        true,
        false,
        null,
        false,
        false,
        false,
        null,
        -1,
        false,
        false,
        0,
        -1,
        9,
        HasMount: true,
        RiderEntityId: 41);

    private static StateSnapshotV2 Snapshot() => new(
        AutomationProtocol.CurrentSnapshotSchemaVersion,
        "capture",
        12,
        1.25,
        new NodeSnapshotV2("client-a", "GameScreen", true, true, "network"),
        new NetworkSnapshotV2("client", "Playing", 3, 0, "none", 0, 0, 0, 0),
        [],
        [],
        [],
        new Dictionary<string, string>());
}
