using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class SnapshotV2Tests {
    [Fact]
    [Trait("Layer", "L0")]
    public void CanonicalHashUsesStablePlayerAndEventOrdering() {
        ActionEventRecord firstEvent = new(
            1,
            DateTimeOffset.UnixEpoch,
            10,
            "a1",
            "client-a",
            AutomationCommandDomain.Player,
            "creativeFly",
            ActionEventPhase.Applied,
            1,
            null,
            "applied",
            new Dictionary<string, string> { ["z"] = "2", ["a"] = "1" });
        ActionEventRecord secondEvent = firstEvent with { Sequence = 2, ActionId = "a2" };
        PlayerSnapshotV2 firstPlayer = Player(1, "A", 1.0000001);
        PlayerSnapshotV2 secondPlayer = Player(2, "B", 2);
        StateSnapshotV2 first = Snapshot(
            [secondPlayer, firstPlayer],
            [secondEvent, firstEvent]);
        StateSnapshotV2 second = Snapshot(
            [firstPlayer, secondPlayer],
            [firstEvent, secondEvent]);

        Assert.Equal(
            SnapshotV2Canonicalizer.ComputeSha256(first),
            SnapshotV2Canonicalizer.ComputeSha256(second));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SnapshotV2ProjectionCommandRoundTripsThroughWireV1() {
        ControlRequest request = ControlProtocol.CreateRequest(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.Events],
                PlayerIndexes: [1, 2],
                EventAfterSequence: 7,
                EventLimit: 64));

        GetSnapshotV2Command command = request.DeserializePayload<GetSnapshotV2Command>();

        Assert.Equal(ControlProtocol.CurrentVersion, request.Version);
        Assert.Equal(
            new[] { SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.Events },
            command.Domains);
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(command.PlayerIndexes));
        Assert.Equal(7, command.EventAfterSequence);
        Assert.Equal(64, command.EventLimit);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CanonicalizerRejectsWrongSchemaBeforeComparison() {
        StateSnapshotV2 snapshot = Snapshot([], []) with { SchemaVersion = 1 };

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SnapshotV2Canonicalizer.Normalize(snapshot));

        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PlayerComparerUsesStableKeysAndVectorTolerance() {
        StateSnapshotV2 expected = Snapshot(
            [Player(2, "B", 2), Player(1, "A", 1)],
            []);
        StateSnapshotV2 actual = Snapshot(
            [Player(1, "A", 1.02), Player(2, "B", 2.01)],
            []);

        SnapshotV2Diff equal = SnapshotV2Comparer.ComparePlayers(
            expected,
            actual,
            new SnapshotV2PlayerComparisonOptions(PositionTolerance: 0.03));
        SnapshotV2Diff different = SnapshotV2Comparer.ComparePlayers(
            expected,
            actual with {
                Players = actual.Players.Select(player =>
                    player.PlayerIndex == 1
                        ? player with { CreativeFlyEnabled = true }
                        : player).ToArray()
            },
            new SnapshotV2PlayerComparisonOptions(PositionTolerance: 0.03));

        Assert.True(equal.Equal);
        SnapshotDifference difference = Assert.Single(different.Differences);
        Assert.Equal("$.players[playerIndex=1].creativeFlyEnabled", difference.Path);
    }

    private static StateSnapshotV2 Snapshot(
        PlayerSnapshotV2[] players,
        ActionEventRecord[] events) =>
        new(
            AutomationProtocol.CurrentSnapshotSchemaVersion,
            "capture",
            12,
            1.234567,
            new NodeSnapshotV2("client-a", "GameScreen", true, true, "network"),
            new NetworkSnapshotV2("client", "Playing", 1, 0, "none", 0, 0, 0, 0),
            players,
            events,
            new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" });

    private static PlayerSnapshotV2 Player(int index, string name, double position) =>
        new(
            index,
            name,
            null,
            index == 1,
            true,
            index,
            new PlayerBodySnapshotV2(
                new Vector3State(position, 2, 3),
                new Vector3State(0, 0, 0),
                false),
            1,
            1,
            false);
}
