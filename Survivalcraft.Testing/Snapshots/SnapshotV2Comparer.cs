namespace Survivalcraft.Testing.Snapshots;

public sealed record SnapshotV2PlayerComparisonOptions(
    double PositionTolerance = 0.05,
    double VelocityTolerance = 0.1,
    double ScalarTolerance = 0.00001,
    bool CompareNetworkIdentity = false,
    double LookAngleTolerance = 0.02,
    double RotationAngleTolerance = 0.03);

public sealed record SnapshotV2TerrainComparisonOptions(
    bool CompareRevision = false);

public sealed record SnapshotV2EntityComparisonOptions(
    double PositionTolerance = 0.2,
    double VelocityTolerance = 0.25,
    double ScalarTolerance = 0.00001,
    double RotationAngleTolerance = 0.05,
    bool CompareRevision = true);

public sealed record SnapshotV2InventoryComparisonOptions(
    bool CompareRevision = true);

public sealed record SnapshotV2HealthComparisonOptions(
    double ScalarTolerance = 0.00001,
    bool CompareRevisions = true,
    bool CompareLifecycleRevision = true);

public sealed record SnapshotV2Diff(
    bool Equal,
    SnapshotDifference[] Differences);

public static class SnapshotV2Comparer {
    public static SnapshotV2Diff CompareInventories(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2InventoryComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2InventoryComparisonOptions();
        List<SnapshotDifference> differences = [];
        Dictionary<int, InventorySnapshotV2> remaining = (actual.Inventories ?? [])
            .ToDictionary(inventory => inventory.InventoryId);
        foreach (InventorySnapshotV2 expectedInventory in (expected.Inventories ?? [])
                     .OrderBy(inventory => inventory.InventoryId)) {
            string path = $"$.inventories[inventoryId={expectedInventory.InventoryId}]";
            if (!remaining.Remove(expectedInventory.InventoryId, out InventorySnapshotV2? actualInventory)) {
                differences.Add(new SnapshotDifference(path, expectedInventory.ToString(), "<missing>"));
                continue;
            }
            CompareValue(path + ".entityId", expectedInventory.EntityId, actualInventory.EntityId, differences);
            CompareValue(path + ".componentType", expectedInventory.ComponentType, actualInventory.ComponentType, differences);
            CompareValue(path + ".kind", expectedInventory.Kind, actualInventory.Kind, differences);
            CompareValue(path + ".slotsCount", expectedInventory.SlotsCount, actualInventory.SlotsCount, differences);
            CompareValue(path + ".activeSlotIndex", expectedInventory.ActiveSlotIndex, actualInventory.ActiveSlotIndex, differences);
            CompareValue(path + ".blockCell", expectedInventory.BlockCell, actualInventory.BlockCell, differences);
            if (options.CompareRevision) {
                CompareValue(path + ".revision", expectedInventory.Revision, actualInventory.Revision, differences);
            }
            Dictionary<int, InventorySlotSnapshotV2> actualSlots = actualInventory.Slots
                .ToDictionary(slot => slot.SlotIndex);
            foreach (InventorySlotSnapshotV2 expectedSlot in expectedInventory.Slots
                         .OrderBy(slot => slot.SlotIndex)) {
                string slotPath = path + $".slots[slotIndex={expectedSlot.SlotIndex}]";
                if (!actualSlots.Remove(expectedSlot.SlotIndex, out InventorySlotSnapshotV2? actualSlot)) {
                    differences.Add(new SnapshotDifference(slotPath, expectedSlot.ToString(), "<missing>"));
                    continue;
                }
                CompareValue(slotPath + ".value", expectedSlot.Value, actualSlot.Value, differences);
                CompareValue(slotPath + ".count", expectedSlot.Count, actualSlot.Count, differences);
                CompareValue(slotPath + ".contents", expectedSlot.Contents, actualSlot.Contents, differences);
                CompareValue(slotPath + ".data", expectedSlot.Data, actualSlot.Data, differences);
            }
            foreach (InventorySlotSnapshotV2 extra in actualSlots.Values.OrderBy(slot => slot.SlotIndex)) {
                differences.Add(new SnapshotDifference(
                    path + $".slots[slotIndex={extra.SlotIndex}]",
                    "<missing>",
                    extra.ToString()));
            }
        }
        foreach (InventorySnapshotV2 extra in remaining.Values.OrderBy(inventory => inventory.InventoryId)) {
            differences.Add(new SnapshotDifference(
                $"$.inventories[inventoryId={extra.InventoryId}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    public static SnapshotV2Diff CompareHealthStates(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2HealthComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2HealthComparisonOptions();
        ValidateTolerance(options.ScalarTolerance, nameof(options.ScalarTolerance));
        List<SnapshotDifference> differences = [];
        Dictionary<int, HealthStateSnapshotV2> remaining = (actual.HealthStates ?? [])
            .ToDictionary(state => state.EntityId);
        foreach (HealthStateSnapshotV2 expectedState in (expected.HealthStates ?? [])
                     .OrderBy(state => state.EntityId)) {
            string path = $"$.healthStates[entityId={expectedState.EntityId}]";
            if (!remaining.Remove(expectedState.EntityId, out HealthStateSnapshotV2? actualState)) {
                differences.Add(new SnapshotDifference(path, expectedState.ToString(), "<missing>"));
                continue;
            }
            CompareValue(path + ".exists", expectedState.Exists, actualState.Exists, differences);
            CompareValue(path + ".templateName", expectedState.TemplateName, actualState.TemplateName, differences);
            CompareValue(path + ".isPlayer", expectedState.IsPlayer, actualState.IsPlayer, differences);
            CompareValue(path + ".playerIndex", expectedState.PlayerIndex, actualState.PlayerIndex, differences);
            CompareNullableScalar(path + ".health", expectedState.Health, actualState.Health, options.ScalarTolerance, differences);
            CompareValue(path + ".isDead", expectedState.IsDead, actualState.IsDead, differences);
            CompareValue(path + ".hasDeathTime", expectedState.HasDeathTime, actualState.HasDeathTime, differences);
            CompareValue(path + ".causeOfDeath", expectedState.CauseOfDeath, actualState.CauseOfDeath, differences);
            CompareNullableScalar(path + ".hitpoints", expectedState.Hitpoints, actualState.Hitpoints, options.ScalarTolerance, differences);
            if (options.CompareRevisions) {
                CompareValue(path + ".healthRevision", expectedState.HealthRevision, actualState.HealthRevision, differences);
                CompareValue(path + ".damageRevision", expectedState.DamageRevision, actualState.DamageRevision, differences);
            }
            if (options.CompareLifecycleRevision) {
                CompareValue(path + ".lifecycleRevision", expectedState.LifecycleRevision, actualState.LifecycleRevision, differences);
            }
        }
        foreach (HealthStateSnapshotV2 extra in remaining.Values.OrderBy(state => state.EntityId)) {
            differences.Add(new SnapshotDifference(
                $"$.healthStates[entityId={extra.EntityId}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    public static SnapshotV2Diff CompareEntities(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2EntityComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2EntityComparisonOptions();
        ValidateTolerance(options.PositionTolerance, nameof(options.PositionTolerance));
        ValidateTolerance(options.VelocityTolerance, nameof(options.VelocityTolerance));
        ValidateTolerance(options.ScalarTolerance, nameof(options.ScalarTolerance));
        ValidateTolerance(options.RotationAngleTolerance, nameof(options.RotationAngleTolerance));

        List<SnapshotDifference> differences = [];
        Dictionary<int, EntitySnapshotV2> remaining = (actual.Entities ?? [])
            .ToDictionary(entity => entity.EntityId);
        foreach (EntitySnapshotV2 expectedEntity in (expected.Entities ?? [])
                     .OrderBy(entity => entity.EntityId)) {
            string path = $"$.entities[entityId={expectedEntity.EntityId}]";
            if (!remaining.Remove(expectedEntity.EntityId, out EntitySnapshotV2? actualEntity)) {
                differences.Add(new SnapshotDifference(path, expectedEntity.ToString(), "<missing>"));
                continue;
            }
            CompareValue(path + ".exists", expectedEntity.Exists, actualEntity.Exists, differences);
            CompareValue(path + ".templateName", expectedEntity.TemplateName, actualEntity.TemplateName, differences);
            CompareValue(path + ".isCreature", expectedEntity.IsCreature, actualEntity.IsCreature, differences);
            CompareValue(path + ".isPlayer", expectedEntity.IsPlayer, actualEntity.IsPlayer, differences);
            CompareValue(path + ".isTombstone", expectedEntity.IsTombstone, actualEntity.IsTombstone, differences);
            if (options.CompareRevision) {
                CompareValue(path + ".revision", expectedEntity.Revision, actualEntity.Revision, differences);
            }
            CompareNullableScalar(
                path + ".health",
                expectedEntity.Health,
                actualEntity.Health,
                options.ScalarTolerance,
                differences);
            CompareNullableScalar(
                path + ".hitpoints",
                expectedEntity.Hitpoints,
                actualEntity.Hitpoints,
                options.ScalarTolerance,
                differences);
            CompareEntityBody(
                path + ".body",
                expectedEntity.Body,
                actualEntity.Body,
                options,
                differences);
        }
        foreach (EntitySnapshotV2 extra in remaining.Values.OrderBy(entity => entity.EntityId)) {
            differences.Add(new SnapshotDifference(
                $"$.entities[entityId={extra.EntityId}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    public static SnapshotV2Diff CompareTerrainCells(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2TerrainComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2TerrainComparisonOptions();
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
            if (options.CompareRevision) {
                CompareValue(path + ".revision", expectedCell.Revision, actualCell.Revision, differences);
            }
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

    private static void CompareEntityBody(
        string path,
        EntityBodySnapshotV2? expected,
        EntityBodySnapshotV2? actual,
        SnapshotV2EntityComparisonOptions options,
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
