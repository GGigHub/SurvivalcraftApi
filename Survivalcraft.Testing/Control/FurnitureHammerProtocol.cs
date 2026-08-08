using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public enum FurnitureHammerFixtureMode {
    Valid,
    MissingTool,
    OutOfRange,
    InvalidSource
}

public enum FurnitureHammerExpectedResult {
    Accepted,
    InvalidRequest,
    RateLimited,
    InvalidDesign,
    InvalidTarget,
    ToolNotEquipped,
    ToolBroken,
    OutOfRange,
    InvalidSource,
    DesignMismatch,
    RegistryFull,
    CommitFailed,
    ClientStateUnavailable
}

public sealed record PrepareFurnitureHammerFixtureCommand(
    int PlayerIndex,
    FurnitureHammerFixtureMode Mode = FurnitureHammerFixtureMode.Valid,
    TerrainCellCoordinate? SourceCell = null);

public sealed record ApplyFurnitureHammerFixtureCommand(
    FurnitureHammerFixture Fixture);

public sealed record FurnitureHammerFixture(
    int PlayerIndex,
    int PlayerEntityId,
    int PlayerInventoryId,
    FurnitureHammerFixtureMode Mode,
    TerrainCellCoordinate SourceCell,
    Vector3State PlayerPosition,
    int SourceValue,
    int DesignMaterialValue,
    int EquippedValue,
    int InitialFurniturePickableCount,
    int InitialFurnitureDesignCount);

public sealed record FurnitureHammerCommand(
    int PlayerIndex,
    FurnitureHammerFixture Fixture,
    FurnitureHammerExpectedResult ExpectedResult = FurnitureHammerExpectedResult.Accepted,
    bool ForgeDesign = false,
    bool ProbeRateLimit = false,
    int DuplicateCount = 1);
