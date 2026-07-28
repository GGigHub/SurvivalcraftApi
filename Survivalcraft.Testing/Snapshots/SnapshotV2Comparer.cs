namespace Survivalcraft.Testing.Snapshots;

public sealed record SnapshotV2PlayerComparisonOptions(
    double PositionTolerance = 0.05,
    double VelocityTolerance = 0.1,
    double ScalarTolerance = 0.00001,
    bool CompareNetworkIdentity = false,
    double LookAngleTolerance = 0.02,
    double RotationAngleTolerance = 0.03);

public sealed record SnapshotV2Diff(
    bool Equal,
    SnapshotDifference[] Differences);

public static class SnapshotV2Comparer {
    public static SnapshotV2Diff CompareTerrainCells(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        List<SnapshotDifference> differences = [];
        Dictionary<(int X, int Y, int Z), TerrainCellSnapshotV2> remaining = actual.TerrainCells
            .ToDictionary(cell => (cell.X, cell.Y, cell.Z));
        foreach (TerrainCellSnapshotV2 expectedCell in expected.TerrainCells
                     .OrderBy(cell => cell.X)
                     .ThenBy(cell => cell.Y)
                     .ThenBy(cell => cell.Z)) {
            string path = $"$.terrainCells[x={expectedCell.X},y={expectedCell.Y},z={expectedCell.Z}]";
            if (!remaining.Remove(
                    (expectedCell.X, expectedCell.Y, expectedCell.Z),
                    out TerrainCellSnapshotV2? actualCell)) {
                differences.Add(new SnapshotDifference(path, expectedCell.ToString(), "<missing>"));
                continue;
            }
            CompareValue(path + ".isLoaded", expectedCell.IsLoaded, actualCell.IsLoaded, differences);
            CompareValue(path + ".value", expectedCell.Value, actualCell.Value, differences);
            CompareValue(path + ".contents", expectedCell.Contents, actualCell.Contents, differences);
            CompareValue(path + ".data", expectedCell.Data, actualCell.Data, differences);
        }
        foreach (TerrainCellSnapshotV2 extra in remaining.Values
                     .OrderBy(cell => cell.X)
                     .ThenBy(cell => cell.Y)
                     .ThenBy(cell => cell.Z)) {
            differences.Add(new SnapshotDifference(
                $"$.terrainCells[x={extra.X},y={extra.Y},z={extra.Z}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    public static SnapshotV2Diff ComparePlayers(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2PlayerComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2PlayerComparisonOptions();
        ValidateTolerance(options.PositionTolerance, nameof(options.PositionTolerance));
        ValidateTolerance(options.VelocityTolerance, nameof(options.VelocityTolerance));
        ValidateTolerance(options.ScalarTolerance, nameof(options.ScalarTolerance));
        ValidateTolerance(options.LookAngleTolerance, nameof(options.LookAngleTolerance));
        ValidateTolerance(options.RotationAngleTolerance, nameof(options.RotationAngleTolerance));

        List<SnapshotDifference> differences = [];
        Dictionary<int, PlayerSnapshotV2> remaining = actual.Players
            .ToDictionary(player => player.PlayerIndex);
        foreach (PlayerSnapshotV2 expectedPlayer in expected.Players
                     .OrderBy(player => player.PlayerIndex)) {
            string path = $"$.players[playerIndex={expectedPlayer.PlayerIndex}]";
            if (!remaining.Remove(expectedPlayer.PlayerIndex, out PlayerSnapshotV2? actualPlayer)) {
                differences.Add(new SnapshotDifference(path, expectedPlayer.ToString(), "<missing>"));
                continue;
            }
            CompareValue(path + ".name", expectedPlayer.Name, actualPlayer.Name, differences);
            CompareValue(
                path + ".readyForPlaying",
                expectedPlayer.ReadyForPlaying,
                actualPlayer.ReadyForPlaying,
                differences);
            CompareValue(
                path + ".creativeFlyEnabled",
                expectedPlayer.CreativeFlyEnabled,
                actualPlayer.CreativeFlyEnabled,
                differences);
            CompareNullableScalar(
                path + ".health",
                expectedPlayer.Health,
                actualPlayer.Health,
                options.ScalarTolerance,
                differences);
            CompareScalar(
                path + ".level",
                expectedPlayer.Level,
                actualPlayer.Level,
                options.ScalarTolerance,
                differences);
            if (options.CompareNetworkIdentity) {
                CompareValue(
                    path + ".networkIdentity",
                    expectedPlayer.NetworkIdentity,
                    actualPlayer.NetworkIdentity,
                    differences);
            }
            CompareBody(path + ".body", expectedPlayer.Body, actualPlayer.Body, options, differences);
        }
        foreach (PlayerSnapshotV2 extra in remaining.Values.OrderBy(player => player.PlayerIndex)) {
            differences.Add(new SnapshotDifference(
                $"$.players[playerIndex={extra.PlayerIndex}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    private static void CompareBody(
        string path,
        PlayerBodySnapshotV2? expected,
        PlayerBodySnapshotV2? actual,
        SnapshotV2PlayerComparisonOptions options,
        List<SnapshotDifference> differences) {
        if (expected == null || actual == null) {
            if (expected != actual) {
                differences.Add(new SnapshotDifference(
                    path,
                    expected?.ToString() ?? "null",
                    actual?.ToString() ?? "null"));
            }
            return;
        }
        CompareVector(
            path + ".position",
            expected.Position,
            actual.Position,
            options.PositionTolerance,
            differences);
        CompareVector(
            path + ".velocity",
            expected.Velocity,
            actual.Velocity,
            options.VelocityTolerance,
            differences);
        CompareValue(path + ".isCrouching", expected.IsCrouching, actual.IsCrouching, differences);
        CompareNullableVector(
            path + ".lookAngles",
            expected.LookAngles,
            actual.LookAngles,
            options.LookAngleTolerance,
            differences);
        CompareNullableRotation(
            path + ".rotation",
            expected.Rotation,
            actual.Rotation,
            options.RotationAngleTolerance,
            differences);
    }

    private static void CompareNullableRotation(
        string path,
        QuaternionState? expected,
        QuaternionState? actual,
        double tolerance,
        List<SnapshotDifference> differences) {
        if (expected == null || actual == null) {
            if (expected != actual) {
                differences.Add(new SnapshotDifference(
                    path,
                    expected?.ToString() ?? "null",
                    actual?.ToString() ?? "null"));
            }
            return;
        }
        double expectedLength = Math.Sqrt(
            expected.X * expected.X
            + expected.Y * expected.Y
            + expected.Z * expected.Z
            + expected.W * expected.W);
        double actualLength = Math.Sqrt(
            actual.X * actual.X
            + actual.Y * actual.Y
            + actual.Z * actual.Z
            + actual.W * actual.W);
        if (expectedLength <= double.Epsilon || actualLength <= double.Epsilon) {
            differences.Add(new SnapshotDifference(
                path,
                expected.ToString(),
                $"{actual}; invalid quaternion length"));
            return;
        }
        double dot = Math.Abs(
            (expected.X * actual.X
             + expected.Y * actual.Y
             + expected.Z * actual.Z
             + expected.W * actual.W)
            / (expectedLength * actualLength));
        double angle = 2 * Math.Acos(Math.Clamp(dot, 0, 1));
        if (angle > tolerance) {
            differences.Add(new SnapshotDifference(
                path,
                expected.ToString(),
                $"{actual}; angle={angle:R}; tolerance={tolerance:R}"));
        }
    }

    private static void CompareNullableVector(
        string path,
        Vector2State? expected,
        Vector2State? actual,
        double tolerance,
        List<SnapshotDifference> differences) {
        if (expected == null || actual == null) {
            if (expected != actual) {
                differences.Add(new SnapshotDifference(
                    path,
                    expected?.ToString() ?? "null",
                    actual?.ToString() ?? "null"));
            }
            return;
        }
        double x = expected.X - actual.X;
        double y = expected.Y - actual.Y;
        double distance = Math.Sqrt(x * x + y * y);
        if (distance > tolerance) {
            differences.Add(new SnapshotDifference(
                path,
                expected.ToString(),
                $"{actual}; distance={distance:R}; tolerance={tolerance:R}"));
        }
    }

    private static void CompareVector(
        string path,
        Vector3State expected,
        Vector3State actual,
        double tolerance,
        List<SnapshotDifference> differences) {
        double x = expected.X - actual.X;
        double y = expected.Y - actual.Y;
        double z = expected.Z - actual.Z;
        double distance = Math.Sqrt(x * x + y * y + z * z);
        if (distance > tolerance) {
            differences.Add(new SnapshotDifference(
                path,
                expected.ToString(),
                $"{actual}; distance={distance:R}; tolerance={tolerance:R}"));
        }
    }

    private static void CompareNullableScalar(
        string path,
        double? expected,
        double? actual,
        double tolerance,
        List<SnapshotDifference> differences) {
        if (!expected.HasValue || !actual.HasValue) {
            if (expected != actual) {
                differences.Add(new SnapshotDifference(
                    path,
                    expected?.ToString("R") ?? "null",
                    actual?.ToString("R") ?? "null"));
            }
            return;
        }
        CompareScalar(path, expected.Value, actual.Value, tolerance, differences);
    }

    private static void CompareScalar(
        string path,
        double expected,
        double actual,
        double tolerance,
        List<SnapshotDifference> differences) {
        if (Math.Abs(expected - actual) > tolerance) {
            differences.Add(new SnapshotDifference(
                path,
                expected.ToString("R"),
                $"{actual:R}; tolerance={tolerance:R}"));
        }
    }

    private static void CompareValue<T>(
        string path,
        T expected,
        T actual,
        List<SnapshotDifference> differences) {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
            differences.Add(new SnapshotDifference(
                path,
                expected?.ToString() ?? "null",
                actual?.ToString() ?? "null"));
        }
    }

    private static void ValidateTolerance(double value, string parameterName) {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
