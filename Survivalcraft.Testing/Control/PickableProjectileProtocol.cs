using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public sealed record PreparePickableProjectileFixtureCommand(
    int ActorPlayerIndex,
    string PersistentAlias = "persistent",
    int PersistentCount = 3);

public sealed record PickableProjectileFixture(
    int ActorPlayerIndex,
    int ActorEntityId,
    int ActorInventoryId,
    string PersistentAlias,
    int PersistentPickableId,
    int PersistentValue,
    int PersistentCount,
    long PersistentRevision,
    Vector3State PersistentPosition,
    Vector3State ActorPosition,
    int LoadedCrossbowValue,
    int LoadedCrossbowCount);

public sealed record ApplyPickableProjectileFixtureCommand(
    PickableProjectileFixture Fixture);

public sealed record AttractPickableCommand(
    string Alias,
    Vector3State TargetPosition);

public sealed record PlayerFireProjectileCommand(
    int PlayerIndex,
    Vector3State? Direction = null,
    int HoldFrames = 4);

public sealed record RunPickableLifecycleBurstCommand(
    string TombstoneAlias = "tombstone",
    bool SendStaleCreate = true);

public sealed record PickableLifecycleBurstResult(
    ActionReceipt Receipt,
    string TombstoneAlias,
    int TombstonePickableId,
    int Value,
    long StaleCreateRevision,
    long FinalRemovalRevision);
