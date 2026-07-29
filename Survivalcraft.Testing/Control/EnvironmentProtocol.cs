namespace Survivalcraft.Testing.Control;

public enum EnvironmentChangeKind {
    TimeOfDay,
    PrecipitationStart,
    PrecipitationEnd,
    FogStart,
    FogEnd,
    Lightning
}

public sealed record SetEnvironmentFixtureCommand(
    double TimeOfDayOffset = 0.25,
    float TimeOfYear = 0.5f,
    bool PrecipitationStarted = false,
    bool FogStarted = false,
    bool CreativeMode = true);

public sealed record SetEnvironmentFixtureResult(
    double TimeOfDayOffset,
    float TimeOfYear,
    bool PrecipitationStarted,
    bool FogStarted,
    long TimeRevision,
    long WeatherRevision);

public sealed record EnvironmentRequestChangeCommand(
    int PlayerIndex,
    EnvironmentChangeKind Change,
    double? TimeOfDayOffset = null,
    bool ForgeAuthoritativeState = false,
    bool ExpectRejected = false);

public sealed record RunEnvironmentRevisionBurstCommand(
    bool FinalPrecipitationStarted = true,
    bool SendStaleSnapshot = true);

public sealed record EnvironmentRevisionBurstResult(
    ActionReceipt Receipt,
    bool FinalPrecipitationStarted,
    long StaleRevision,
    long FinalRevision);
