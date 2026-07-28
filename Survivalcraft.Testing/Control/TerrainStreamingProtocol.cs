using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public sealed record PrepareTerrainStreamingFixtureCommand(
    int ActorPlayerIndex,
    int ObserverPlayerIndex);

public sealed record TerrainStreamingFixture(
    int ActorPlayerIndex,
    int ObserverPlayerIndex,
    Vector3State ActorPosition,
    Vector3State ObserverPosition,
    TerrainCellCoordinate ProbeCell,
    int InitialValue,
    int[] RevisionValues,
    int StandingValue,
    double VisibilityDistance,
    double ContentDistance,
    Vector3State MoveInput,
    int MoveHoldFrames);

public sealed record ApplyTerrainStreamingViewCommand(
    int PlayerIndex,
    Vector3State Position,
    int StandingValue,
    double VisibilityDistance,
    double ContentDistance,
    bool FreezeBody = true,
    bool ArmMoveThreshold = false);

public sealed record RunTerrainRevisionBurstCommand(
    TerrainCellCoordinate Cell,
    int[] Values,
    bool SendStaleSnapshot = true);

public sealed record TerrainRevisionBurstResult(
    ActionReceipt Receipt,
    long BaselineRevision,
    long FinalRevision,
    int FinalValue,
    int ChangeCount);
