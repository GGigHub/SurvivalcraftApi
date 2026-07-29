using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class LogicSnapshotV2Tests {
    [Fact]
    [Trait("Layer", "L0")]
    public void CanonicalizerSortsAndRoundsLogicDomains() {
        StateSnapshotV2 snapshot = CreateSnapshot(
            logic: [
                new LogicElementStateSnapshotV2(5, 70, 2, "switch", 1, 141, 1, "on=True", 3),
                new LogicElementStateSnapshotV2(1, 70, 2, "battery", 2, 138, 2, "voltage=13", 4)
            ],
            voltages: [
                new PersistentVoltageSnapshotV2(5, 70, 2, 0.12345678),
                new PersistentVoltageSnapshotV2(1, 70, 2, 0.98765432)
            ],
            moving: [
                new MovingBlockStateSnapshotV2(
                    "zeta", false, null, 2, true, 1, 1,
                    new Vector3State(1.1234567, 2, 3),
                    new Vector3State(4, 5, 6),
                    new Vector3State(0.1234567, 0, 0),
                    1,
                    "0,0,0:15"),
                new MovingBlockStateSnapshotV2(
                    "alpha", true, new TerrainCellCoordinate(4, 70, 5), 1, true, 2, 1,
                    new Vector3State(7, 8, 9),
                    new Vector3State(10, 11, 12),
                    new Vector3State(0, 0, 0),
                    1,
                    "0,0,0:15")
            ]);

        StateSnapshotV2 normalized = SnapshotV2Canonicalizer.Normalize(snapshot, 5);
        LogicElementStateSnapshotV2[] logic = normalized.LogicStates!;
        PersistentVoltageSnapshotV2[] voltages = normalized.PersistentVoltages!;
        MovingBlockStateSnapshotV2[] moving = normalized.MovingBlocks!;

        Assert.Equal([1, 5], logic.Select(state => state.X));
        Assert.Equal([1, 5], voltages.Select(state => state.X));
        Assert.Equal(0.98765, voltages[0].Voltage);
        Assert.Equal(["alpha", "zeta"], moving.Select(state => state.Identity));
        Assert.Equal(1.12346, moving[1].Position.X);
        Assert.Equal(0.12346, moving[1].Velocity.X);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LogicComparerReportsStateGenerationAndVoltageDifferences() {
        StateSnapshotV2 expected = CreateSnapshot(
            logic: [
                new LogicElementStateSnapshotV2(1, 70, 2, "battery", 2, 138, 2, "voltage=13", 4)
            ],
            voltages: [new PersistentVoltageSnapshotV2(1, 70, 2, 0.8)],
            moving: [
                new MovingBlockStateSnapshotV2(
                    "moving", true, new TerrainCellCoordinate(1, 70, 2), 1, true, 2, 1,
                    new Vector3State(1, 2, 3),
                    new Vector3State(4, 5, 6),
                    new Vector3State(0, 0, 0),
                    1,
                    "0,0,0:15")
            ]);
        StateSnapshotV2 actual = CreateSnapshot(
            logic: [
                new LogicElementStateSnapshotV2(1, 70, 2, "battery", 3, 138, 3, "voltage=12", 5)
            ],
            voltages: [new PersistentVoltageSnapshotV2(1, 70, 2, 0.7)],
            moving: [
                new MovingBlockStateSnapshotV2(
                    "moving", true, new TerrainCellCoordinate(1, 70, 2), 1, true, 3, 1,
                    new Vector3State(1, 2, 3),
                    new Vector3State(4, 5, 6),
                    new Vector3State(0, 0, 0),
                    1,
                    "0,0,0:15")
            ]);

        SnapshotV2Diff diff = SnapshotV2Comparer.CompareLogic(expected, actual);

        Assert.False(diff.Equal);
        Assert.Contains(diff.Differences, item => item.Path.EndsWith(".state", StringComparison.Ordinal));
        Assert.Contains(diff.Differences, item => item.Path.EndsWith(".voltage", StringComparison.Ordinal));
        Assert.Contains(diff.Differences, item => item.Path.EndsWith(".generation", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LogicComparerKeepsConcurrentUntaggedInstancesSeparate() {
        MovingBlockStateSnapshotV2 first = Moving("collapse", instanceId: 11, x: 1);
        MovingBlockStateSnapshotV2 second = Moving("collapse", instanceId: 12, x: 3);
        StateSnapshotV2 expected = CreateSnapshot([], [], [first, second]);
        StateSnapshotV2 actual = CreateSnapshot([], [], [second, first]);

        SnapshotV2Diff equal = SnapshotV2Comparer.CompareLogic(expected, actual);
        SnapshotV2Diff missing = SnapshotV2Comparer.CompareLogic(
            expected,
            CreateSnapshot([], [], [first]));

        Assert.True(equal.Equal);
        Assert.False(missing.Equal);
        Assert.Contains(missing.Differences, difference =>
            difference.Path.Contains("instance=12", StringComparison.Ordinal));
    }

    private static MovingBlockStateSnapshotV2 Moving(
        string identity,
        long instanceId,
        double x) =>
        new(
            identity,
            false,
            null,
            instanceId,
            true,
            1,
            1,
            new Vector3State(x, 2, 3),
            new Vector3State(x + 4, 5, 6),
            new Vector3State(0, 0, 0),
            1,
            "0,0,0:15");

    private static StateSnapshotV2 CreateSnapshot(
        LogicElementStateSnapshotV2[] logic,
        PersistentVoltageSnapshotV2[] voltages,
        MovingBlockStateSnapshotV2[] moving) =>
        new(
            AutomationProtocol.CurrentSnapshotSchemaVersion,
            "capture",
            1,
            1,
            new NodeSnapshotV2("test", "none", true, true, "network"),
            null,
            [],
            [],
            [],
            new Dictionary<string, string>(),
            LogicStates: logic,
            PersistentVoltages: voltages,
            MovingBlocks: moving,
            LogicRevisions: new LogicRevisionMetricsSnapshotV2(0, 0, 0, 0, 0, 0, 0, 0, 1));
}
