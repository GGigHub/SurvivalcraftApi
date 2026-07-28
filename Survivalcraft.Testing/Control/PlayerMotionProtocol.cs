using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public sealed record PreparePlayerMotionFixtureCommand(
    int PlayerIndex,
    Vector3State? StartPosition = null);

public sealed record ApplyPlayerMotionFixtureCommand(
    PlayerMotionFixture Fixture);

public sealed record PlayerMotionFixture(
    int PlayerIndex,
    Vector3State StartPosition,
    Vector2State InitialLookAngles,
    bool InitialCrouching,
    int StandingValue);

public sealed record PlayerMoveCommand(
    int PlayerIndex,
    Vector3State Input,
    int HoldFrames = 45);

public sealed record PlayerLookCommand(
    int PlayerIndex,
    Vector2State Input,
    int HoldFrames = 8);

public sealed record PlayerSetCrouchCommand(
    int PlayerIndex,
    bool Enabled);
