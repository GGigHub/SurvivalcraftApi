using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class PickableProjectileProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void AutoTestV8PickableAndProjectileCommandsRoundTrip() {
        PreparePickableProjectileFixtureCommand prepare = new(3, "persistent", 7);
        AttractPickableCommand attraction = new(
            "persistent",
            new Vector3State(10.5, 71.2, -4.5));
        PlayerFireProjectileCommand fire = new(
            3,
            new Vector3State(0.1, 0.2, 0.9),
            HoldFrames: 6);
        RunPickableLifecycleBurstCommand burst = new("tombstone", true);

        Assert.Equal(13, AutomationProtocol.CurrentApiVersion);
        Assert.Equal(
            prepare,
            Request(ControlCommandNames.PreparePickableProjectileFixture, prepare)
                .DeserializePayload<PreparePickableProjectileFixtureCommand>());
        Assert.Equal(
            attraction,
            Request(ControlCommandNames.AttractPickable, attraction)
                .DeserializePayload<AttractPickableCommand>());
        Assert.Equal(
            fire,
            Request(ControlCommandNames.PlayerFireProjectile, fire)
                .DeserializePayload<PlayerFireProjectileCommand>());
        Assert.Equal(
            burst,
            Request(ControlCommandNames.RunPickableLifecycleBurst, burst)
                .DeserializePayload<RunPickableLifecycleBurstCommand>());
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SnapshotCanonicalizationOrdersAndRoundsTransientWorldItems() {
        StateSnapshotV2 snapshot = Snapshot() with {
            Pickables = [
                new PickableSnapshotV2(
                    9,
                    true,
                    2,
                    3,
                    new Vector3State(9.1234567, 2, 3),
                    new Vector3State(0.1234567, 0, 0),
                    new Vector3State(10.7654321, 2, 3),
                    4,
                    false),
                new PickableSnapshotV2(
                    2,
                    false,
                    0,
                    0,
                    new Vector3State(0, 0, 0),
                    new Vector3State(0, 0, 0),
                    null,
                    5,
                    true)
            ],
            Projectiles = [
                new ProjectileSnapshotV2(
                    7,
                    11,
                    new Vector3State(7.1234567, 2, 3),
                    new Vector3State(1.7654321, 0, 0),
                    new Vector3State(0, 0.3333333, 0),
                    41,
                    false,
                    false),
                new ProjectileSnapshotV2(
                    3,
                    12,
                    new Vector3State(3, 2, 1),
                    new Vector3State(0, 0, 0),
                    new Vector3State(0, 0, 0),
                    null,
                    false,
                    true)
            ]
        };

        StateSnapshotV2 normalized = SnapshotV2Canonicalizer.Normalize(snapshot, 5);
        PickableSnapshotV2[] pickables = Assert.IsType<PickableSnapshotV2[]>(
            normalized.Pickables);
        ProjectileSnapshotV2[] projectiles = Assert.IsType<ProjectileSnapshotV2[]>(
            normalized.Projectiles);

        Assert.Equal([2, 9], pickables.Select(item => item.PickableId));
        Assert.Equal(9.12346, pickables[1].Position.X);
        Assert.Equal(10.76543, pickables[1].FlyToPosition!.X);
        Assert.Equal([3L, 7L], projectiles.Select(item => item.EventId));
        Assert.Equal(7.12346, projectiles[1].Position.X);
        Assert.Equal(0.33333, projectiles[1].AngularVelocity.Y);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ReplayParserPreservesStrictCrossDomainActionSequence() {
        AttractPickableCommand attraction = new(
            "persistent",
            new Vector3State(12, 70, 4));
        PlayerFireProjectileCommand fire = new(1, HoldFrames: 4);
        RunPickableLifecycleBurstCommand burst = new("tombstone", true);
        ScenarioActionRecordV2[] records = [
            Action("server", "attract", ControlCommandNames.AttractPickable, attraction),
            Action("client-a", "fire", ControlCommandNames.PlayerFireProjectile, fire),
            Action("server", "burst", ControlCommandNames.RunPickableLifecycleBurst, burst)
        ];

        PickableProjectileReplayPlan replay =
            PickableProjectileMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(attraction, replay.Attraction);
        Assert.Equal(fire, replay.FireProjectile);
        Assert.Equal(burst, replay.LifecycleBurst);
        Assert.Throws<InvalidDataException>(() =>
            PickableProjectileMultiplayerScenario.ParseReplayActions([
                records[1],
                records[0],
                records[2]
            ]));
        Assert.Throws<InvalidDataException>(() =>
            PickableProjectileMultiplayerScenario.ParseReplayActions([
                records[0],
                records[1],
                records[2] with {
                    Payload = JsonSerializer.SerializeToElement(
                        burst with { SendStaleCreate = false },
                        ControlProtocol.JsonOptions)
                }
            ]));
    }

    private static ControlRequest Request<T>(string command, T payload) =>
        ControlProtocol.CreateRequest(command, payload);

    private static ScenarioActionRecordV2 Action<T>(
        string role,
        string actionId,
        string command,
        T payload) =>
        new(
            role,
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            new ActionReceipt(
                actionId,
                true,
                1,
                role == "client-a" ? 1 : -1,
                "accepted",
                Provenance: role == "client-a"
                    ? ActionProvenance.ProductionInput
                    : ActionProvenance.AuthoritativeState,
                ProductionEntry: "test-production-entry"));

    private static StateSnapshotV2 Snapshot() =>
        new(
            AutomationProtocol.CurrentSnapshotSchemaVersion,
            "capture",
            1,
            1,
            new NodeSnapshotV2("unit", "GameScreen", true, true, "World"),
            null,
            [],
            [],
            [],
            new Dictionary<string, string>());
}
