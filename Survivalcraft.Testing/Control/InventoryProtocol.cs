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
    int ChestItemCount);

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
