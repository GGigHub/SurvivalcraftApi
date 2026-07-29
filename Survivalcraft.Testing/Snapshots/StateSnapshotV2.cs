using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Snapshots;

public enum SnapshotDomain {
    Network,
    Players,
    Terrain,
    Entities,
    Events,
    Inventories,
    Health,
    Environment,
    Mechanics,
    Logic
}

public sealed record GetSnapshotV2Command(
    SnapshotDomain[] Domains,
    int[]? PlayerIndexes = null,
    TerrainCellCoordinate[]? TerrainCells = null,
    long EventAfterSequence = 0,
    int EventLimit = 128,
    bool IncludeVolatileDiagnostics = false,
    int[]? EntityIds = null,
    int[]? InventoryIds = null,
    TerrainCellCoordinate[]? LogicCells = null);

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
    EntityLifecycleSnapshotV2? EntityLifecycle = null,
    InventorySnapshotV2[]? Inventories = null,
    HealthStateSnapshotV2[]? HealthStates = null,
    StateRevisionMetricsSnapshotV2? StateRevisions = null,
    EnvironmentSnapshotV2? Environment = null,
    MechanicsStateSnapshotV2[]? MechanicsStates = null,
    LogicElementStateSnapshotV2[]? LogicStates = null,
    PersistentVoltageSnapshotV2[]? PersistentVoltages = null,
    MovingBlockStateSnapshotV2[]? MovingBlocks = null,
    LogicRevisionMetricsSnapshotV2? LogicRevisions = null);

public sealed record LogicElementStateSnapshotV2(
    int X,
    int Y,
    int Z,
    string Kind,
    int Value,
    int Contents,
    int Data,
    string State,
    long Revision);

public sealed record PersistentVoltageSnapshotV2(
    int X,
    int Y,
    int Z,
    double Voltage);

public sealed record MovingBlockStateSnapshotV2(
    string Identity,
    bool HasTag,
    TerrainCellCoordinate? Tag,
    long InstanceId,
    bool Present,
    long Generation,
    long Revision,
    Vector3State Position,
    Vector3State TargetPosition,
    Vector3State Velocity,
    int BlockCount,
    string Blocks);

public sealed record LogicRevisionMetricsSnapshotV2(
    int AuthoritativeTrackedStates,
    int ClientTrackedStates,
    long AppliedStates,
    long IgnoredStaleStates,
    long IgnoredLifecycleStates,
    int AuthoritativeMovingGenerations,
    int ClientMovingGenerations,
    int MovingTombstones,
    long ElectricityRevision);

public sealed record EnvironmentSnapshotV2(
    double TotalElapsedGameTime,
    double TimeOfDayOffset,
    float TimeOfYear,
    double PrecipitationStartTime,
    double PrecipitationEndTime,
    float PrecipitationRampTime,
    float LightningIntensity,
    bool IsPrecipitationStarted,
    double FogStartTime,
    double FogEndTime,
    float FogRampTime,
    float FogProgress,
    bool IsFogStarted,
    double LastLightningStrikeTime,
    Vector3State? LightningStrikePosition,
    long TimeRevision,
    long WeatherRevision,
    long SkyRevision,
    string GameMode);

public sealed record MechanicsStateSnapshotV2(
    int EntityId,
    bool Exists,
    bool IsPlayer,
    int? PlayerIndex,
    bool HasSleep,
    bool IsSleeping,
    bool AllowManualWakeup,
    double? SleepStartTime,
    long SleepRevision,
    bool HasOnFire,
    bool IsOnFire,
    double FireDuration,
    long FireRevision,
    long LifecycleRevision,
    bool HasRider = false,
    int? MountEntityId = null,
    bool MountAnimating = false,
    bool IsDismounting = false,
    long MountRevision = -1,
    long MountEntityLifecycleRevision = -1,
    bool HasMount = false,
    int? RiderEntityId = null);

public sealed record InventorySnapshotV2(
    int InventoryId,
    int? EntityId,
    string ComponentType,
    string Kind,
    int SlotsCount,
    int ActiveSlotIndex,
    long Revision,
    InventorySlotSnapshotV2[] Slots,
    TerrainCellCoordinate? BlockCell = null);

public sealed record InventorySlotSnapshotV2(
    int SlotIndex,
    int Value,
    int Count,
    int Contents,
    int Data);

public sealed record HealthStateSnapshotV2(
    int EntityId,
    bool Exists,
    string? TemplateName,
    bool IsPlayer,
    int? PlayerIndex,
    double? Health,
    bool IsDead,
    bool HasDeathTime,
    string? CauseOfDeath,
    long HealthRevision,
    double? Hitpoints,
    long DamageRevision,
    long LifecycleRevision);

public sealed record StateRevisionMetricsSnapshotV2(
    int AuthoritativeTrackedStates,
    int ClientTrackedStates,
    long AppliedStates,
    long IgnoredStaleStates,
    long IgnoredLifecycleStates = 0);

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
