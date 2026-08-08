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
    Logic,
    Pickables,
    Projectiles
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
    TerrainCellCoordinate[]? LogicCells = null,
    int[]? PickableIds = null,
    long[]? ProjectileEventIds = null);

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
    LogicRevisionMetricsSnapshotV2? LogicRevisions = null,
    PickableSnapshotV2[]? Pickables = null,
    PickableLifecycleSnapshotV2? PickableLifecycle = null,
    ProjectileSnapshotV2[]? Projectiles = null,
    ProjectileEventSnapshotV2? ProjectileEvents = null,
    WorldFireStateSnapshotV2[]? WorldFires = null,
    WorldFireMetricsSnapshotV2? WorldFireMetrics = null,
    ExplosionEventSnapshotV2? ExplosionEvents = null);

public sealed record WorldFireStateSnapshotV2(
    TerrainCellCoordinate Cell,
    bool Present,
    bool RuntimePresent,
    float Expandability,
    long Revision,
    bool IsTombstone);

public sealed record WorldFireMetricsSnapshotV2(
    int AuthoritativeTrackedCells,
    int ClientTrackedCells,
    long AppliedAdds,
    long AppliedRemovals,
    long IgnoredStaleStates,
    long RejectedClientStates,
    int Tombstones);

public sealed record ExplosionEventSnapshotV2(
    long GeneratedEvents,
    long GeneratedCellChunks,
    long GeneratedCells,
    long GeneratedSoundEvents,
    long AcceptedEvents,
    long AcceptedCellChunks,
    long AppliedCells,
    long AcceptedSoundEvents,
    long IgnoredDuplicateParts,
    long IgnoredExpiredParts,
    long HighestGeneratedEventId,
    long HighestAcceptedEventId,
    int RememberedEvents);

public sealed record PickableSnapshotV2(
    int PickableId,
    bool Exists,
    int Value,
    int Count,
    Vector3State Position,
    Vector3State Velocity,
    Vector3State? FlyToPosition,
    long Revision,
    bool IsTombstone);

public sealed record PickableLifecycleSnapshotV2(
    int AuthoritativeTrackedPickables,
    int ClientTrackedPickables,
    long AppliedCreates,
    long AppliedUpdates,
    long AppliedRemovals,
    long IgnoredStalePackets,
    long MissingUpdateRequests,
    int Tombstones);

public sealed record ProjectileSnapshotV2(
    long EventId,
    int Value,
    Vector3State Position,
    Vector3State Velocity,
    Vector3State AngularVelocity,
    int? OwnerEntityId,
    bool IsIncendiary,
    bool ToRemove);

public sealed record ProjectileEventSnapshotV2(
    long GeneratedEvents,
    long AcceptedEvents,
    long IgnoredDuplicateEvents,
    long IgnoredExpiredEvents,
    long HighestGeneratedEventId,
    long HighestAcceptedEventId,
    int RememberedEventIds);

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
    TerrainCellCoordinate? BlockCell = null,
    FurnaceStateSnapshotV2? FurnaceState = null);

public sealed record FurnaceStateSnapshotV2(
    double FireTimeRemaining,
    double SmeltingProgress,
    double HeatLevel,
    long Revision,
    int IngredientSlots,
    int FuelSlotIndex,
    int ResultSlotIndex,
    int RemainsSlotIndex);

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
    int FaultPending,
    IReadOnlyDictionary<string, long>? QueuedPacketsByType = null,
    IReadOnlyDictionary<string, long>? FaultTransmissionsByType = null,
    IReadOnlyDictionary<string, long>? FaultBytesByType = null,
    NetworkSessionSnapshotV2[]? Sessions = null,
    NetworkSessionLifecycleSnapshotV2? SessionLifecycle = null,
    ModNetworkRuntimeSnapshotV2? ModChannel = null);

public sealed record NetworkSessionSnapshotV2(
    string SessionId,
    string TransportKind,
    string CompatibilityState,
    string? CompatibilityCode,
    string AdmissionState,
    string? AdmissionCode,
    string? NetworkIdentity,
    int PlayerIndex,
    string NetworkState,
    string ModChannelCatalogFingerprint,
    int PlayerCreateRequests,
    int DuplicatePlayerCreateRequests,
    int PlayerEntityRequests);

public sealed record NetworkSessionLifecycleEventSnapshotV2(
    long Sequence,
    string SessionId,
    string TransportKind,
    string Kind,
    string? NetworkIdentity,
    int PlayerIndex,
    string Detail);

public sealed record NetworkSessionLifecycleSnapshotV2(
    long Connected,
    long CompatibilityAccepted,
    long CompatibilityRejected,
    long AdmissionAccepted,
    long AdmissionRejected,
    long Superseded,
    long PlayerAssigned,
    long DuplicatePlayerCreateIgnored,
    long PlayerEntityRequested,
    long Disconnected,
    NetworkSessionLifecycleEventSnapshotV2[] Events);

public sealed record ModNetworkRuntimeSnapshotV2(
    string CatalogFingerprint,
    long Sent,
    long Received,
    long Delivered,
    long Dropped,
    long Duplicates,
    long RateLimited,
    long HandlerErrors,
    int ActivePeerStates);

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
