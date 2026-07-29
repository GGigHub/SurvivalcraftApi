using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public enum LogicElementKind {
    AdjustableDelayGate,
    Battery,
    Button,
    Switch,
    Piston,
    MemoryBank,
    TruthTable,
    Sign,
    Dispenser
}

public sealed record PrepareLogicFixtureCommand(
    int PlayerIndex,
    string Alias = "primary");

public sealed record ApplyLogicFixtureCommand(LogicFixture Fixture);

public sealed record LogicFixtureElement(
    LogicElementKind Kind,
    TerrainCellCoordinate Cell,
    int InitialData,
    int TargetValue,
    string? TargetStructuredData = null,
    byte TargetLastOutput = 0,
    string[]? TargetSignLines = null,
    string? TargetSignUrl = null);

public sealed record LogicFixture(
    string Alias,
    int PlayerIndex,
    int PlayerEntityId,
    Vector3State PlayerPosition,
    LogicFixtureElement[] Elements,
    TerrainCellCoordinate PistonPushCell,
    TerrainCellCoordinate FarBatteryCell,
    TerrainCellCoordinate PersistentVoltageCell,
    int InitialSwitchData,
    int ToggledSwitchData);

public sealed record LogicEditCommand(
    int PlayerIndex,
    LogicElementKind Kind,
    TerrainCellCoordinate Cell,
    int Value = 0,
    string? StructuredData = null,
    byte LastOutput = 0,
    string[]? SignLines = null,
    string? SignUrl = null,
    bool ForgeAuthoritativeState = false,
    bool ExpectRejected = false,
    int DuplicateCount = 1,
    long? ExpectedRevision = null,
    long? TerrainRevision = null);

public sealed record LogicInteractCommand(
    int PlayerIndex,
    TerrainCellCoordinate Cell,
    int? TargetPlayerIndex = null,
    bool ExpectRejected = false,
    int DuplicateCount = 1);

public sealed record RunLogicRevisionBurstCommand(
    TerrainCellCoordinate BatteryCell,
    TerrainCellCoordinate? PersistentVoltageCell = null,
    int IntermediateVoltage = 2,
    int FinalVoltage = 13,
    float IntermediatePersistentVoltage = 0.2f,
    float FinalPersistentVoltage = 0.8f,
    bool SendStaleState = true);

public sealed record LogicRevisionBurstResult(
    ActionReceipt Receipt,
    TerrainCellCoordinate BatteryCell,
    TerrainCellCoordinate PersistentVoltageCell,
    int FinalVoltage,
    float FinalPersistentVoltage,
    long StaleLogicRevision,
    long FinalLogicRevision,
    long StaleElectricityRevision,
    long FinalElectricityRevision);

public sealed record RunMovingBlockLifecycleBurstCommand(
    TerrainCellCoordinate AnchorCell,
    string Identity = "AutoTest.LogicMoving",
    bool SendStaleAdd = true);

public sealed record MovingBlockLifecycleBurstResult(
    ActionReceipt Receipt,
    string Identity,
    string ConcurrentIdentity,
    int ConcurrentInstances,
    long StaleGeneration,
    long FinalGeneration,
    long FinalRevision,
    bool ReplacementPresent);

public sealed record RunPistonLifecycleCommand(
    TerrainCellCoordinate PistonCell,
    int Extension = 1,
    bool Stop = true);

public sealed record PistonLifecycleResult(
    ActionReceipt Receipt,
    TerrainCellCoordinate PistonCell,
    bool MovingObserved,
    bool PresentAfterCommand,
    long Generation,
    long Revision);
