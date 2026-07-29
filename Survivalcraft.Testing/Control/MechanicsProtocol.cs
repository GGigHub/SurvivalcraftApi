using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public sealed record PrepareMechanicsFixtureCommand(int PlayerIndex);

public sealed record MechanicsFixture(
    int PlayerIndex,
    int PlayerEntityId,
    bool IsSleeping,
    bool IsOnFire);

public sealed record SetPlayerFireFixtureCommand(
    int EntityId,
    float Duration = 30f,
    bool Extinguish = false);

public sealed record SetPlayerFireFixtureResult(
    int EntityId,
    bool IsOnFire,
    float Duration,
    long Revision);

public sealed record PlayerSleepCommand(
    int PlayerIndex,
    bool Sleep,
    bool AllowManualWakeup = true,
    int? TargetEntityId = null,
    bool ExpectRejected = false);

public sealed record PlayerIgniteCommand(
    int PlayerIndex,
    int TargetEntityId,
    float Duration = 30f,
    bool ExpectRejected = true);

public sealed record RunMechanicsRevisionBurstCommand(
    int EntityId,
    float IntermediateDuration = 180f,
    float FinalDuration = 120f,
    bool SendStaleSnapshot = true);

public sealed record MechanicsRevisionBurstResult(
    ActionReceipt Receipt,
    int EntityId,
    float FinalDuration,
    long StaleRevision,
    long FinalRevision);

public sealed record PrepareMountFixtureCommand(
    int PlayerIndex,
    string Alias = "primary",
    string TemplateName = "Boat",
    double Distance = 1.0);

public sealed record MountFixture(
    string Alias,
    string TemplateName,
    int PlayerIndex,
    int RiderEntityId,
    long RiderLifecycleRevision,
    int MountEntityId,
    long MountLifecycleRevision,
    long MountRevision,
    Vector3State Position);

public sealed record PlayerMountCommand(
    int PlayerIndex,
    int MountEntityId,
    bool Mount,
    int? TargetRiderEntityId = null,
    bool ForgeAuthoritativeState = false,
    bool ExpectRejected = false);

public sealed record RunMountRevisionBurstCommand(
    int RiderEntityId,
    int MountEntityId,
    bool FinalMounted = true,
    bool SendStaleState = true);

public sealed record MountRevisionBurstResult(
    ActionReceipt Receipt,
    int RiderEntityId,
    int MountEntityId,
    bool FinalMounted,
    long StaleRevision,
    long FinalRevision);

public sealed record RunMountLifecycleBurstCommand(
    string Alias,
    int RiderEntityId,
    bool ReuseEntityId = true,
    bool SendStaleState = true);

public sealed record MountLifecycleBurstResult(
    ActionReceipt Receipt,
    MountFixture Replacement,
    long StaleMountLifecycleRevision,
    long FinalMountLifecycleRevision,
    long AuthoritativeMountRevision,
    long InjectedStaleRevision);
