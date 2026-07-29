using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Snapshots;

public enum SnapshotDomain {
    Network,
    Players,
    Terrain,
    Entities,
    Events
}

public sealed record GetSnapshotV2Command(
    SnapshotDomain[] Domains,
    int[]? PlayerIndexes = null,
    TerrainCellCoordinate[]? TerrainCells = null,
    long EventAfterSequence = 0,
    int EventLimit = 128,
    bool IncludeVolatileDiagnostics = false,
    int[]? EntityIds = null);

public sealed record StateSnapshotV2(
    int SchemaVersion,
    string CaptureId,
    long CaptureTick,
    double TimeSeconds,
    NodeSnapshotV2 Node,
    NetworkSnapshotV2? Network,
    PlayerSnapshotV2[] Players,
    TerrainCellSnapshotV2[] TerrainCells,
    ActionEventRecord[] RecentEvents,
    IReadOnlyDictionary<string, string> Diagnostics,
    TerrainStreamingSnapshotV2? TerrainStreaming = null,
    EntitySnapshotV2[]? Entities = null,
    EntityLifecycleSnapshotV2? EntityLifecycle = null);

public sealed record EntitySnapshotV2(
    int EntityId,
    bool Exists,
    string? TemplateName,
    bool IsCreature,
    bool IsPlayer,
    EntityBodySnapshotV2? Body,
    double? Health,
    double? Hitpoints,
    long Revision,
    bool IsTombstone);

public sealed record EntityBodySnapshotV2(
    Vector3State Position,
    Vector3State Velocity,
    QuaternionState Rotation);

public sealed record EntityLifecycleSnapshotV2(
    int AuthoritativeTrackedEntities,
    int ClientTrackedEntities,
    long AppliedLoads,
    long AppliedRemovals,
    long IgnoredStalePackets,
    int Tombstones);

public sealed record TerrainCellSnapshotV2(
    int X,
    int Y,
    int Z,
    int Value,
    int Contents,
    int Data,
    bool IsLoaded,
    long Revision = -1,
    string? ChunkState = null);

public sealed record TerrainStreamingSnapshotV2(
    TerrainUpdateLocationSnapshotV2[] Locations,
    int AllocatedChunkCount,
    int ReadyChunkCount,
    int PendingChunkCount,
    int SentChunkCount,
    int AuthoritativeTrackedChunks,
    int ClientTrackedChunks,
    long AppliedSnapshots,
    long AppliedChanges,
    long IgnoredStalePackets,
    long IgnoredUnloadedChanges,
    long BufferedChanges,
    long BufferOverflows,
    int PendingRevisionChanges);

public sealed record TerrainUpdateLocationSnapshotV2(
    int PlayerIndex,
    Vector2State Center,
    double VisibilityDistance,
    double ContentDistance,
    int PendingChunks,
    int SentChunks);

public sealed record NodeSnapshotV2(
    string Role,
    string Screen,
    bool ContentLoaded,
    bool ProjectLoaded,
    string ProjectKind);

public sealed record NetworkSnapshotV2(
    string Mode,
    string State,
    int LocalPlayerIndex,
    int ServerSessionCount,
    string RelayMode,
    long FaultSent,
    long FaultDropped,
    long FaultDuplicated,
    int FaultPending);

public sealed record PlayerSnapshotV2(
    int PlayerIndex,
    string Name,
    string? NetworkIdentity,
    bool IsMainPlayer,
    bool ReadyForPlaying,
    int? EntityId,
    PlayerBodySnapshotV2? Body,
    double? Health,
    double Level,
    bool CreativeFlyEnabled);

public sealed record PlayerBodySnapshotV2(
    Vector3State Position,
    Vector3State Velocity,
    bool IsCrouching,
    Vector2State? LookAngles = null,
    QuaternionState? Rotation = null);
