using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public sealed record PrepareHealthFixtureCommand(
    int PlayerIndex,
    double TargetHealth = 0.2);

public sealed record ApplyHealthFixtureCommand(
    HealthFixture Fixture);

public sealed record HealthFixture(
    int PlayerIndex,
    int PlayerEntityId,
    int TargetEntityId,
    Vector3State PlayerPosition,
    Vector3State TargetPosition,
    int WeaponValue,
    double TargetHealth);

public sealed record SetHealthFixtureCommand(
    int EntityId,
    double Health = 1,
    bool Kill = false,
    string Cause = "AutoTest");

public sealed record SetHealthFixtureResult(
    int EntityId,
    double Health,
    bool IsDead,
    long Revision);

public sealed record PlayerHitCommand(
    int PlayerIndex,
    int TargetEntityId,
    bool ExpectRejected = false);

public sealed record PlayerRequestRespawnCommand(
    int PlayerIndex,
    bool ExpectRejected = false);

public sealed record RunHealthRevisionBurstCommand(
    int EntityId,
    double IntermediateHealth = 0.8,
    double FinalHealth = 0.6,
    bool SendStaleSnapshot = true);

public sealed record HealthRevisionBurstResult(
    ActionReceipt Receipt,
    int EntityId,
    double FinalHealth,
    long StaleRevision,
    long FinalRevision);
