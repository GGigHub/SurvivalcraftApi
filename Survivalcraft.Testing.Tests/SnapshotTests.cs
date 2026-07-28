using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class SnapshotTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void CanonicalHashIgnoresCollectionEnumerationOrder() {
        StateSnapshot first = CreateSnapshot(
            players: [
                new PlayerState(2, "B", false, true, new Vector3State(2, 3, 4), null, 1),
                new PlayerState(1, "A", true, true, new Vector3State(1.0000001, 2, 3), null, 1)
            ],
            cells: [
                new TerrainCellState(4, 5, 6, 7, 8, 9),
                new TerrainCellState(1, 2, 3, 4, 5, 6)
            ]);
        StateSnapshot second = CreateSnapshot(
            players: first.Players.Reverse().ToArray(),
            cells: first.TerrainCells.Reverse().ToArray());

        Assert.Equal(
            SnapshotCanonicalizer.ComputeSha256(first),
            SnapshotCanonicalizer.ComputeSha256(second));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DiffReportsExactNormalizedPath() {
        StateSnapshot expected = CreateSnapshot(
            players: [],
            cells: [new TerrainCellState(1, 2, 3, 4, 5, 6)]);
        StateSnapshot actual = expected with {
            TerrainCells = [new TerrainCellState(1, 2, 3, 99, 5, 6)]
        };

        SnapshotDiff diff = SnapshotComparer.Compare(expected, actual);

        Assert.False(diff.Equal);
        SnapshotDifference difference = Assert.Single(diff.Differences);
        Assert.Equal("$.terrainCells[0].value", difference.Path);
        Assert.Equal("4", difference.Expected);
        Assert.Equal("99", difference.Actual);
    }

    private static StateSnapshot CreateSnapshot(
        PlayerState[] players,
        TerrainCellState[] cells) =>
        new(
            1,
            "test",
            12,
            1.234567,
            "GameScreen",
            "Playing",
            new WorldState("World", "data:doc/Worlds/1", 42),
            players,
            [],
            cells,
            new Dictionary<string, string> {
                ["z"] = "last",
                ["a"] = "first"
            });
}
