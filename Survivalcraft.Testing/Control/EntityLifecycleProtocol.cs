using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public sealed record PrepareEntityLifecycleFixtureCommand(
    int ActorPlayerIndex,
    string Alias = "survivor",
    string TemplateName = "Cow");

public sealed record EntityLifecycleFixture(
    string Alias,
    int EntityId,
    string TemplateName,
    Vector3State Position,
    long Revision);

public sealed record ApplyEntityImpulseCommand(
    string Alias,
    Vector3State Impulse,
    int ActiveFrames = 12);

public sealed record RunEntityLifecycleBurstCommand(
    string ReplacementAlias = "replacement",
    string TombstoneAlias = "tombstone",
    bool SendStaleLoads = true);

public sealed record EntityLifecycleBurstResult(
    ActionReceipt Receipt,
    EntityLifecycleFixture Replacement,
    int TombstoneEntityId,
    long StaleReplacementRevision,
    long FinalReplacementRevision,
    long StaleTombstoneRevision,
    long FinalTombstoneRevision);
