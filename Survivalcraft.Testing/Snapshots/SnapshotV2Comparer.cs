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
    bool CompareRevision = true,
    double FurnaceScalarTolerance = 0.001,
    bool CompareFurnaceRevision = false);

public sealed record SnapshotV2HealthComparisonOptions(
    double ScalarTolerance = 0.00001,
    bool CompareRevisions = true,
    bool CompareLifecycleRevision = true);

public sealed record SnapshotV2MechanicsComparisonOptions(
    double ScalarTolerance = 0.00001,
    bool CompareRevisions = true,
    bool CompareLifecycleRevisions = true);

public sealed record SnapshotV2LogicComparisonOptions(
    double VoltageTolerance = 0.00001,
    double PositionTolerance = 0.1,
    double VelocityTolerance = 0.1,
    bool CompareRevisions = true);

public sealed record SnapshotV2Diff(
    bool Equal,
    SnapshotDifference[] Differences);

public static class SnapshotV2Comparer {
    public static SnapshotV2Diff CompareLogic(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2LogicComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2LogicComparisonOptions();
        ValidateTolerance(options.VoltageTolerance, nameof(options.VoltageTolerance));
        ValidateTolerance(options.PositionTolerance, nameof(options.PositionTolerance));
        ValidateTolerance(options.VelocityTolerance, nameof(options.VelocityTolerance));

        List<SnapshotDifference> differences = [];
        Dictionary<(int X, int Y, int Z), LogicElementStateSnapshotV2> logic =
            (actual.LogicStates ?? []).ToDictionary(state => (state.X, state.Y, state.Z));
        foreach (LogicElementStateSnapshotV2 expectedState in (expected.LogicStates ?? [])
                     .OrderBy(state => state.X)
                     .ThenBy(state => state.Y)
                     .ThenBy(state => state.Z)) {
            (int X, int Y, int Z) key = (expectedState.X, expectedState.Y, expectedState.Z);
            string path = $"$.logicStates[x={key.X},y={key.Y},z={key.Z}]";
            if (!logic.Remove(key, out LogicElementStateSnapshotV2? actualState)) {
                differences.Add(new SnapshotDifference(path, expectedState.ToString(), "<missing>"));
                continue;
            }
            CompareValue(path + ".kind", expectedState.Kind, actualState.Kind, differences);
            CompareValue(path + ".value", expectedState.Value, actualState.Value, differences);
            CompareValue(path + ".contents", expectedState.Contents, actualState.Contents, differences);
            CompareValue(path + ".data", expectedState.Data, actualState.Data, differences);
            CompareValue(path + ".state", expectedState.State, actualState.State, differences);
            if (options.CompareRevisions) {
                CompareValue(path + ".revision", expectedState.Revision, actualState.Revision, differences);
            }
        }
        foreach (LogicElementStateSnapshotV2 extra in logic.Values
                     .OrderBy(state => state.X)
                     .ThenBy(state => state.Y)
                     .ThenBy(state => state.Z)) {
            differences.Add(new SnapshotDifference(
                $"$.logicStates[x={extra.X},y={extra.Y},z={extra.Z}]",
                "<missing>",
                extra.ToString()));
        }

        Dictionary<(int X, int Y, int Z), PersistentVoltageSnapshotV2> voltages =
            (actual.PersistentVoltages ?? []).ToDictionary(state => (state.X, state.Y, state.Z));
        foreach (PersistentVoltageSnapshotV2 expectedState in (expected.PersistentVoltages ?? [])
                     .OrderBy(state => state.X)
                     .ThenBy(state => state.Y)
                     .ThenBy(state => state.Z)) {
            (int X, int Y, int Z) key = (expectedState.X, expectedState.Y, expectedState.Z);
            string path = $"$.persistentVoltages[x={key.X},y={key.Y},z={key.Z}]";
            if (!voltages.Remove(key, out PersistentVoltageSnapshotV2? actualState)) {
                differences.Add(new SnapshotDifference(path, expectedState.ToString(), "<missing>"));
                continue;
            }
            CompareScalar(
                path + ".voltage",
                expectedState.Voltage,
                actualState.Voltage,
                options.VoltageTolerance,
                differences);
        }
        foreach (PersistentVoltageSnapshotV2 extra in voltages.Values
                     .OrderBy(state => state.X)
                     .ThenBy(state => state.Y)
                     .ThenBy(state => state.Z)) {
            differences.Add(new SnapshotDifference(
                $"$.persistentVoltages[x={extra.X},y={extra.Y},z={extra.Z}]",
                "<missing>",
                extra.ToString()));
        }

        Dictionary<(string Identity, bool HasTag, TerrainCellCoordinate? Tag, long InstanceId), MovingBlockStateSnapshotV2>
            moving = (actual.MovingBlocks ?? []).ToDictionary(
                state => (state.Identity, state.HasTag, state.Tag, state.InstanceId));
        foreach (MovingBlockStateSnapshotV2 expectedState in (expected.MovingBlocks ?? [])
                     .OrderBy(state => state.Identity, StringComparer.Ordinal)
                     .ThenBy(state => state.Tag?.X ?? 0)
                     .ThenBy(state => state.Tag?.Y ?? 0)
                     .ThenBy(state => state.Tag?.Z ?? 0)
                     .ThenBy(state => state.InstanceId)) {
            (string Identity, bool HasTag, TerrainCellCoordinate? Tag, long InstanceId) key =
                (expectedState.Identity, expectedState.HasTag, expectedState.Tag, expectedState.InstanceId);
            string path = $"$.movingBlocks[identity={key.Identity},tag={key.Tag},instance={key.InstanceId}]";
            if (!moving.Remove(key, out MovingBlockStateSnapshotV2? actualState)) {
                differences.Add(new SnapshotDifference(path, expectedState.ToString(), "<missing>"));
                continue;
            }
            CompareValue(path + ".present", expectedState.Present, actualState.Present, differences);
            CompareValue(path + ".generation", expectedState.Generation, actualState.Generation, differences);
            if (options.CompareRevisions) {
                CompareValue(path + ".revision", expectedState.Revision, actualState.Revision, differences);
            }
            CompareVector(
                path + ".position",
                expectedState.Position,
                actualState.Position,
                options.PositionTolerance,
                differences);
            CompareVector(
                path + ".targetPosition",
                expectedState.TargetPosition,
                actualState.TargetPosition,
                options.PositionTolerance,
                differences);
            CompareVector(
                path + ".velocity",
                expectedState.Velocity,
                actualState.Velocity,
                options.VelocityTolerance,
                differences);
            CompareValue(path + ".blockCount", expectedState.BlockCount, actualState.BlockCount, differences);
            CompareValue(path + ".blocks", expectedState.Blocks, actualState.Blocks, differences);
        }
        foreach (MovingBlockStateSnapshotV2 extra in moving.Values
                     .OrderBy(state => state.Identity, StringComparer.Ordinal)
                     .ThenBy(state => state.InstanceId)) {
            differences.Add(new SnapshotDifference(
                $"$.movingBlocks[identity={extra.Identity},tag={extra.Tag},instance={extra.InstanceId}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    public static SnapshotV2Diff CompareMechanicsStates(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2MechanicsComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2MechanicsComparisonOptions();
        ValidateTolerance(options.ScalarTolerance, nameof(options.ScalarTolerance));

        List<SnapshotDifference> differences = [];
        Dictionary<int, MechanicsStateSnapshotV2> remaining = (actual.MechanicsStates ?? [])
            .ToDictionary(state => state.EntityId);
        foreach (MechanicsStateSnapshotV2 expectedState in (expected.MechanicsStates ?? [])
                     .OrderBy(state => state.EntityId)) {
            string path = $"$.mechanicsStates[entityId={expectedState.EntityId}]";
            if (!remaining.Remove(expectedState.EntityId, out MechanicsStateSnapshotV2? actualState)) {
                differences.Add(new SnapshotDifference(path, expectedState.ToString(), "<missing>"));
                continue;
            }

            CompareValue(path + ".exists", expectedState.Exists, actualState.Exists, differences);
            CompareValue(path + ".isPlayer", expectedState.IsPlayer, actualState.IsPlayer, differences);
            CompareValue(path + ".playerIndex", expectedState.PlayerIndex, actualState.PlayerIndex, differences);
            CompareValue(path + ".hasSleep", expectedState.HasSleep, actualState.HasSleep, differences);
            CompareValue(path + ".isSleeping", expectedState.IsSleeping, actualState.IsSleeping, differences);
            CompareValue(path + ".allowManualWakeup", expectedState.AllowManualWakeup, actualState.AllowManualWakeup, differences);
            CompareNullableScalar(path + ".sleepStartTime", expectedState.SleepStartTime, actualState.SleepStartTime, options.ScalarTolerance, differences);
            CompareValue(path + ".hasOnFire", expectedState.HasOnFire, actualState.HasOnFire, differences);
            CompareValue(path + ".isOnFire", expectedState.IsOnFire, actualState.IsOnFire, differences);
            CompareScalar(path + ".fireDuration", expectedState.FireDuration, actualState.FireDuration, options.ScalarTolerance, differences);
            CompareValue(path + ".hasRider", expectedState.HasRider, actualState.HasRider, differences);
            CompareValue(path + ".mountEntityId", expectedState.MountEntityId, actualState.MountEntityId, differences);
            CompareValue(path + ".mountAnimating", expectedState.MountAnimating, actualState.MountAnimating, differences);
            CompareValue(path + ".isDismounting", expectedState.IsDismounting, actualState.IsDismounting, differences);
            CompareValue(path + ".hasMount", expectedState.HasMount, actualState.HasMount, differences);
            CompareValue(path + ".riderEntityId", expectedState.RiderEntityId, actualState.RiderEntityId, differences);
            if (options.CompareRevisions) {
                CompareValue(path + ".sleepRevision", expectedState.SleepRevision, actualState.SleepRevision, differences);
                CompareValue(path + ".fireRevision", expectedState.FireRevision, actualState.FireRevision, differences);
                CompareValue(path + ".mountRevision", expectedState.MountRevision, actualState.MountRevision, differences);
            }
            if (options.CompareLifecycleRevisions) {
                CompareValue(path + ".lifecycleRevision", expectedState.LifecycleRevision, actualState.LifecycleRevision, differences);
                CompareValue(path + ".mountEntityLifecycleRevision", expectedState.MountEntityLifecycleRevision, actualState.MountEntityLifecycleRevision, differences);
            }
        }
        foreach (MechanicsStateSnapshotV2 extra in remaining.Values.OrderBy(state => state.EntityId)) {
            differences.Add(new SnapshotDifference(
                $"$.mechanicsStates[entityId={extra.EntityId}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    public static SnapshotV2Diff CompareInventories(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual,
        SnapshotV2InventoryComparisonOptions? options = null) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new SnapshotV2InventoryComparisonOptions();
        ValidateTolerance(options.FurnaceScalarTolerance, nameof(options.FurnaceScalarTolerance));
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
            CompareFurnaceState(
                path + ".furnaceState",
                expectedInventory.FurnaceState,
                actualInventory.FurnaceState,
                options,
                differences);
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

    public static SnapshotV2Diff CompareFurnitureDesigns(
        StateSnapshotV2 expected,
        StateSnapshotV2 actual) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        List<SnapshotDifference> differences = [];
        Dictionary<int, FurnitureDesignSnapshotV2> remaining =
            (actual.FurnitureDesigns ?? [])
            .ToDictionary(design => design.DesignIndex);
        foreach (FurnitureDesignSnapshotV2 expectedDesign in
                 (expected.FurnitureDesigns ?? []).OrderBy(design => design.DesignIndex)) {
            string path = $"$.furnitureDesigns[designIndex={expectedDesign.DesignIndex}]";
            if (!remaining.Remove(
                    expectedDesign.DesignIndex,
                    out FurnitureDesignSnapshotV2? actualDesign)) {
                differences.Add(new SnapshotDifference(
                    path,
                    expectedDesign.ToString(),
                    "<missing>"));
                continue;
            }
            CompareValue(path + ".resolution", expectedDesign.Resolution, actualDesign.Resolution, differences);
            CompareValue(path + ".name", expectedDesign.Name, actualDesign.Name, differences);
            CompareValue(path + ".interactionMode", expectedDesign.InteractionMode, actualDesign.InteractionMode, differences);
            CompareValue(path + ".nonEmptyCellCount", expectedDesign.NonEmptyCellCount, actualDesign.NonEmptyCellCount, differences);
            CompareValue(path + ".valueFingerprint", expectedDesign.ValueFingerprint, actualDesign.ValueFingerprint, differences);
        }
        foreach (FurnitureDesignSnapshotV2 extra in remaining.Values
                     .OrderBy(design => design.DesignIndex)) {
            differences.Add(new SnapshotDifference(
                $"$.furnitureDesigns[designIndex={extra.DesignIndex}]",
                "<missing>",
                extra.ToString()));
        }
        return new SnapshotV2Diff(differences.Count == 0, differences.ToArray());
    }

    private static void CompareFurnaceState(
        string path,
        FurnaceStateSnapshotV2? expected,
        FurnaceStateSnapshotV2? actual,
        SnapshotV2InventoryComparisonOptions options,
        List<SnapshotDifference> differences) {
        if (expected == null || actual == null) {
            CompareValue(path, expected, actual, differences);
            return;
        }
        CompareScalar(path + ".fireTimeRemaining", expected.FireTimeRemaining, actual.FireTimeRemaining, options.FurnaceScalarTolerance, differences);
        CompareScalar(path + ".smeltingProgress", expected.SmeltingProgress, actual.SmeltingProgress, options.FurnaceScalarTolerance, differences);
        CompareScalar(path + ".heatLevel", expected.HeatLevel, actual.HeatLevel, options.FurnaceScalarTolerance, differences);
        CompareValue(path + ".ingredientSlots", expected.IngredientSlots, actual.IngredientSlots, differences);
        CompareValue(path + ".fuelSlotIndex", expected.FuelSlotIndex, actual.FuelSlotIndex, differences);
        CompareValue(path + ".resultSlotIndex", expected.ResultSlotIndex, actual.ResultSlotIndex, differences);
        CompareValue(path + ".remainsSlotIndex", expected.RemainsSlotIndex, actual.RemainsSlotIndex, differences);
        if (options.CompareFurnaceRevision) {
            CompareValue(path + ".revision", expected.Revision, actual.Revision, differences);
        }
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
