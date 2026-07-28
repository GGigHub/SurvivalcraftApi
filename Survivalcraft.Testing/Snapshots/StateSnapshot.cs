namespace Survivalcraft.Testing.Snapshots;

public sealed record StateSnapshot(
    int SchemaVersion,
    string Role,
    long CaptureTick,
    double TimeSeconds,
    string Screen,
    string NetworkState,
    WorldState? World,
    PlayerState[] Players,
    EntityState[] Entities,
    TerrainCellState[] TerrainCells,
    IReadOnlyDictionary<string, string> Diagnostics);

public sealed record WorldState(
    string Name,
    string DirectoryName,
    int Seed);

public sealed record PlayerState(
    int PlayerIndex,
    string Name,
    bool IsMainPlayer,
    bool ReadyForPlaying,
    Vector3State? Position,
    Vector3State? Velocity,
    double? Health);

public sealed record EntityState(
    int EntityId,
    string Template,
    Vector3State? Position,
    Vector3State? Velocity);

public sealed record TerrainCellCoordinate(int X, int Y, int Z);

public sealed record TerrainCellState(
    int X,
    int Y,
    int Z,
    int Value,
    int Contents,
    int Data);

public sealed record Vector2State(double X, double Y);

public sealed record Vector3State(double X, double Y, double Z);

public sealed record QuaternionState(double X, double Y, double Z, double W);
