using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public enum PlayerTerrainActionKind {
    Use,
    Place,
    Dig
}

public sealed record PrepareTerrainInteractionFixtureCommand(
    PlayerTerrainActionKind Kind,
    int PlayerIndex,
    TerrainCellCoordinate? TargetCell = null);

public sealed record ApplyTerrainInteractionFixtureCommand(
    TerrainInteractionFixture Fixture);

public sealed record TerrainInteractionFixture(
    PlayerTerrainActionKind Kind,
    int PlayerIndex,
    TerrainCellCoordinate TargetCell,
    TerrainCellCoordinate ExpectedCell,
    int TargetValue,
    int InitialValue,
    int ExpectedValue,
    int ActiveValue,
    int ActiveCount,
    Vector3State PlayerPosition);

public sealed record PlayerUseCommand(
    int PlayerIndex,
    TerrainCellCoordinate TargetCell);

public sealed record PlayerPlaceCommand(
    int PlayerIndex,
    TerrainCellCoordinate TargetCell);

public sealed record PlayerDigCommand(
    int PlayerIndex,
    TerrainCellCoordinate TargetCell,
    int HoldFrames = 240);
