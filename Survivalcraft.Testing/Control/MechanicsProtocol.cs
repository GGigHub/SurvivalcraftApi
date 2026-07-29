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
