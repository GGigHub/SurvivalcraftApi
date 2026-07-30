using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public sealed record PrepareInventoryFixtureCommand(
    int PlayerIndex,
    int PlayerItemCount = 8,
    int ChestItemCount = 2);

public sealed record ApplyInventoryFixtureCommand(
    InventoryFixture Fixture);

public sealed record InventoryFixture(
    int PlayerIndex,
    int PlayerEntityId,
    int PlayerInventoryId,
    int ChestEntityId,
    int ChestInventoryId,
    TerrainCellCoordinate ChestCell,
    Vector3State PlayerPosition,
    int StackItemValue,
    int DropItemValue,
    int PlayerItemCount,
    int ChestItemCount,
    int FurnaceEntityId,
    int FurnaceInventoryId,
    TerrainCellCoordinate FurnaceCell,
    int FurnaceIngredientValue,
    int FurnaceFuelValue,
    int FurnaceResultValue,
    int FurnaceIngredientSlotIndex,
    int FurnaceFuelSlotIndex,
    int FurnaceResultSlotIndex);

public enum InventoryOpenTargetKind {
    ById,
    ByPoint
}

public enum InventoryOpenExpectedResult {
    Accepted,
    InvalidRequest,
    InventoryNotFound,
    PermissionDenied,
    UnsupportedInventory,
    ClientStateUnavailable
}

public sealed record InventoryOpenContainerCommand(
    int PlayerIndex,
    InventoryOpenTargetKind TargetKind,
    InventoryOpenExpectedResult ExpectedResult,
    int InventoryId = 0,
    TerrainCellCoordinate? Cell = null,
    Vector3State? ActorPositionOverride = null);

public sealed record InventoryMoveCommand(
    int PlayerIndex,
    int FromInventoryId,
    int FromSlotIndex,
    int ToInventoryId,
    int ToSlotIndex,
    int Count,
    bool ExpectRejected = false);

public sealed record InventorySelectSlotCommand(
    int PlayerIndex,
    int InventoryId,
    int SlotIndex);

public sealed record InventoryDropCommand(
    int PlayerIndex);

public sealed record RunInventoryRevisionBurstCommand(
    int InventoryId,
    int SlotIndex,
    int IntermediateCount = 1,
    int FinalCount = 5,
    bool SendStaleSnapshot = true);

public sealed record InventoryRevisionBurstResult(
    ActionReceipt Receipt,
    int InventoryId,
    int SlotIndex,
    int FinalValue,
    int FinalCount,
    long StaleRevision,
    long FinalRevision);

public sealed record RunFurnaceRevisionBurstCommand(
    int FurnaceInventoryId,
    float FinalFireTimeRemaining = 0f,
    float FinalSmeltingProgress = 0f,
    float FinalHeatLevel = 0f,
    bool SendStaleSnapshot = true);

public sealed record FurnaceRevisionBurstResult(
    ActionReceipt Receipt,
    int FurnaceInventoryId,
    float FinalFireTimeRemaining,
    float FinalSmeltingProgress,
    float FinalHeatLevel,
    long StaleRevision,
    long FinalRevision);
