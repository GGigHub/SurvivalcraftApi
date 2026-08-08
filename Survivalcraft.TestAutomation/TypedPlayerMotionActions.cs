using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const int MotionCompletionGraceFrames = 120;
    private const float MoveObservationDistance = 0.05f;
    private const float LookObservationDistance = 0.01f;
    private const float RotationObservationAngle = 0.01f;

    private PendingPlayerMotionAction? pendingPlayerMotionAction;

    private enum PlayerMotionActionKind {
        Move,
        Look,
        SetCrouch
    }

    private sealed record PendingPlayerMotionAction(
        string ActionId,
        PlayerMotionActionKind Kind,
        int PlayerIndex,
        int InputEndFrame,
        int DeadlineFrame,
        int HoldFrames,
        Vector3 BaselinePosition,
        Vector2 BaselineLookAngles,
        Quaternion BaselineRotation,
        Vector3 MoveInput,
        Vector2 LookInput,
        bool DesiredCrouch,
        bool Observed,
        int LastCrouchAttemptFrame);

    private PlayerMotionFixture PreparePlayerMotionFixture(
        PreparePlayerMotionFixtureCommand command) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Player motion fixtures must be prepared by the server node.");
        }
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        ComponentPlayer actor = ResolvePlayer(players, command.PlayerIndex, requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The server fixture expects a connected remote player.");
        }

        Vector3 requested = command.StartPosition == null
            ? CreateDefaultMotionStart(players)
            : ToVector(command.StartPosition);
        if (!IsFinite(requested)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Fixture startPosition must contain finite values.");
        }
        int floorY = (int)MathF.Floor(requested.Y) - 1;
        if (floorY is < 1 or > 250) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Fixture startPosition must leave room for a platform and player body.");
        }
        Vector3 start = new(requested.X, floorY + 1.01f, requested.Z);
        int standingValue = Terrain.MakeBlockValue(GraniteBlock.Index);
        BuildMotionPlatform(terrain, start, floorY, standingValue);
        PreparePlayerForMotionFixture(actor, start, standingValue);
        terrain.TerrainUpdater.RequestSynchronousUpdate();

        return new PlayerMotionFixture(
            command.PlayerIndex,
            ToVector(start),
            new Vector2State(0, 0),
            false,
            standingValue);
    }

    private ControlAck ApplyPlayerMotionFixture(ApplyPlayerMotionFixtureCommand command) {
        PlayerMotionFixture fixture = command.Fixture
            ?? throw new AutomationCommandException("invalid_payload", "Fixture payload is required.");
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Player motion actor fixtures must be applied by a connected client node.");
        }
        EnsureNoTypedPlayerActionInProgress();
        if (fixture.StartPosition == null
            || fixture.InitialLookAngles == null
            || fixture.InitialCrouching
            || fixture.StandingValue <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Player motion fixture contains unsupported initial state.");
        }
        Vector3 start = ToVector(fixture.StartPosition);
        Vector2 look = ToVector(fixture.InitialLookAngles);
        if (!IsFinite(start) || !IsFinite(look)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Player motion fixture contains non-finite values.");
        }

        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        ComponentPlayer actor = ResolvePlayer(players, fixture.PlayerIndex, requireMainPlayer: true);
        int floorY = (int)MathF.Floor(start.Y) - 1;
        BuildMotionPlatform(terrain, start, floorY, fixture.StandingValue);
        terrain.TerrainUpdater.RequestSynchronousUpdate();
        PreparePlayerForMotionFixture(actor, start, fixture.StandingValue);
        actor.ComponentLocomotion.LookAngles = look;
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new ControlAck(
            true,
            $"Prepared and synchronized player {fixture.PlayerIndex} for typed motion input.");
    }

    private ActionReceipt QueuePlayerMove(
        AutomationCommandContext context,
        PlayerMoveCommand command) {
        if (command.Input == null) {
            throw new AutomationCommandException("invalid_payload", "Move input is required.");
        }
        ValidateHoldFrames(command.HoldFrames, "Move");
        Vector3 input = ToVector(command.Input);
        float horizontalLengthSquared = input.X * input.X + input.Z * input.Z;
        if (!IsFinite(input)
            || MathF.Abs(input.Y) > 0.0001f
            || horizontalLengthSquared is <= 0.0001f or > 1.0001f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Move input must be a finite, non-zero horizontal vector with length at most 1.");
        }
        ComponentPlayer actor = ResolveMotionActor(command.PlayerIndex);
        PendingPlayerMotionAction pending = CreatePendingMotionAction(
            context,
            actor,
            PlayerMotionActionKind.Move,
            command.HoldFrames,
            moveInput: input);
        pendingPlayerMotionAction = pending;
        RecordMotionEvent(pending, ActionEventPhase.Queued, "queued", actor);
        InputHijackState.HoldMoveInput(input, command.HoldFrames);
        return QueuedReceipt(context, command.PlayerIndex);
    }

    private ActionReceipt QueuePlayerLook(
        AutomationCommandContext context,
        PlayerLookCommand command) {
        if (command.Input == null) {
            throw new AutomationCommandException("invalid_payload", "Look input is required.");
        }
        ValidateHoldFrames(command.HoldFrames, "Look");
        Vector2 input = ToVector(command.Input);
        float lengthSquared = input.LengthSquared();
        if (!IsFinite(input) || lengthSquared is <= 0.0001f or > 1.0001f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Look input must be a finite, non-zero vector with length at most 1.");
        }
        ComponentPlayer actor = ResolveMotionActor(command.PlayerIndex);
        PendingPlayerMotionAction pending = CreatePendingMotionAction(
            context,
            actor,
            PlayerMotionActionKind.Look,
            command.HoldFrames,
            lookInput: input);
        pendingPlayerMotionAction = pending;
        RecordMotionEvent(pending, ActionEventPhase.Queued, "queued", actor);
        InputHijackState.HoldLookInput(input, command.HoldFrames);
        return QueuedReceipt(context, command.PlayerIndex);
    }

    private ActionReceipt QueuePlayerSetCrouch(
        AutomationCommandContext context,
        PlayerSetCrouchCommand command) {
        ComponentPlayer actor = ResolveMotionActor(command.PlayerIndex);
        bool currentTarget = actor.ComponentBody.TargetCrouchFactor > 0f;
        if (currentTarget == command.Enabled
            && actor.ComponentBody.IsCrouching == command.Enabled) {
            PendingPlayerMotionAction alreadyApplied = CreatePendingMotionAction(
                context,
                actor,
                PlayerMotionActionKind.SetCrouch,
                1,
                desiredCrouch: command.Enabled);
            RecordMotionEvent(
                alreadyApplied,
                ActionEventPhase.Queued,
                "queued",
                actor);
            RecordMotionEvent(
                alreadyApplied,
                ActionEventPhase.Applied,
                "already_applied",
                actor);
            return new ActionReceipt(
                context.RequestId,
                true,
                Time.FrameIndex,
                command.PlayerIndex,
                "already_applied",
                Provenance: ActionProvenance.ProductionInput,
                ProductionEntry: ProductionPathEntries.PlayerMotion);
        }

        PendingPlayerMotionAction pending = CreatePendingMotionAction(
            context,
            actor,
            PlayerMotionActionKind.SetCrouch,
            1,
            desiredCrouch: command.Enabled);
        pendingPlayerMotionAction = pending;
        RecordMotionEvent(pending, ActionEventPhase.Queued, "queued", actor);
        pendingPlayerMotionAction = QueueCrouchAttemptIfEligible(pending, actor);
        return QueuedReceipt(context, command.PlayerIndex);
    }

    private PendingPlayerMotionAction CreatePendingMotionAction(
        AutomationCommandContext context,
        ComponentPlayer actor,
        PlayerMotionActionKind kind,
        int holdFrames,
        Vector3 moveInput = default,
        Vector2 lookInput = default,
        bool desiredCrouch = false) {
        EnsureNoTypedPlayerActionInProgress();
        int inputEndFrame = checked(Time.FrameIndex + holdFrames);
        return new PendingPlayerMotionAction(
            context.RequestId,
            kind,
            actor.PlayerData.PlayerIndex,
            inputEndFrame,
            checked(inputEndFrame + MotionCompletionGraceFrames),
            holdFrames,
            actor.ComponentBody.Position,
            actor.ComponentLocomotion.LookAngles,
            actor.ComponentBody.Rotation,
            moveInput,
            lookInput,
            desiredCrouch,
            false,
            -1);
    }

    private ComponentPlayer ResolveMotionActor(int playerIndex) {
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Player motion actions must originate from a connected client node.");
        }
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        return ResolvePlayer(players, playerIndex, requireMainPlayer: true);
    }

    private void HandleTypedBodyUpdate(ComponentPlayer player, bool isMainPlayer) {
        HandleTerrainStreamingMoveTrigger(player, isMainPlayer);
        PendingPlayerMotionAction? pending = pendingPlayerMotionAction;
        if (pending == null
            || !isMainPlayer
            || player.PlayerData.PlayerIndex != pending.PlayerIndex) {
            return;
        }

        bool effectObserved = pending.Kind switch {
            PlayerMotionActionKind.Move => HorizontalDistance(
                pending.BaselinePosition,
                player.ComponentBody.Position) >= MoveObservationDistance,
            PlayerMotionActionKind.Look => LookEffectObserved(pending, player),
            PlayerMotionActionKind.SetCrouch =>
                player.ComponentBody.IsCrouching == pending.DesiredCrouch,
            _ => false
        };
        if (effectObserved && !pending.Observed) {
            pending = pending with { Observed = true };
            pendingPlayerMotionAction = pending;
            RecordMotionEvent(pending, ActionEventPhase.Observed, "production_state_observed", player);
        }

        if (!pending.Observed && pending.Kind == PlayerMotionActionKind.SetCrouch) {
            pending = QueueCrouchAttemptIfEligible(pending, player);
            pendingPlayerMotionAction = pending;
        }

        bool inputFinished = Time.FrameIndex >= pending.InputEndFrame;
        if (pending.Observed
            && (pending.Kind == PlayerMotionActionKind.SetCrouch || inputFinished)) {
            RecordMotionEvent(pending, ActionEventPhase.Applied, "input_applied", player);
            pendingPlayerMotionAction = null;
            InputHijackState.ResetGameplayInputs();
            return;
        }
        if (Time.FrameIndex >= pending.DeadlineFrame) {
            RecordMotionEvent(pending, ActionEventPhase.Rejected, "effect_not_observed", player);
            pendingPlayerMotionAction = null;
            InputHijackState.ResetGameplayInputs();
        }
    }

    private static PendingPlayerMotionAction QueueCrouchAttemptIfEligible(
        PendingPlayerMotionAction pending,
        ComponentPlayer actor) {
        const int retryIntervalFrames = 6;
        bool targetMatches = (actor.ComponentBody.TargetCrouchFactor > 0f)
            == pending.DesiredCrouch;
        bool canToggle = !pending.DesiredCrouch
            || actor.ComponentBody.StandingOnValue.HasValue;
        if (!targetMatches
            && canToggle
            && Time.FrameIndex - pending.LastCrouchAttemptFrame >= retryIntervalFrames) {
            InputHijackState.QueueToggleCrouch();
            return pending with { LastCrouchAttemptFrame = Time.FrameIndex };
        }
        return pending;
    }

    private void EnsureNoTypedPlayerActionInProgress() {
        if (pendingTerrainAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingTerrainAction.ActionId}' is still in progress.");
        }
        if (pendingPlayerMotionAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingPlayerMotionAction.ActionId}' is still in progress.");
        }
        if (pendingInventoryAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingInventoryAction.ActionId}' is still in progress.");
        }
        if (pendingContainerOpenAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingContainerOpenAction.ActionId}' is still in progress.");
        }
        if (pendingFurnitureHammerAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingFurnitureHammerAction.ActionId}' is still in progress.");
        }
        if (pendingHealthAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingHealthAction.ActionId}' is still in progress.");
        }
        if (pendingProjectileAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingProjectileAction.ActionId}' is still in progress.");
        }
    }

    private static void PreparePlayerForMotionFixture(
        ComponentPlayer player,
        Vector3 position,
        int standingValue) {
        ComponentBody body = player.ComponentBody;
        body.Position = position;
        body.Rotation = Quaternion.Identity;
        body.Velocity = Vector3.Zero;
        body.TargetCrouchFactor = 0f;
        body.CrouchFactor = 0f;
        body.IsGravityEnabled = true;
        body.IsGroundDragEnabled = true;
        body.IsSmoothRiseEnabled = true;
        body.StandingOnValue = standingValue;
        ComponentLocomotion locomotion = player.ComponentLocomotion;
        locomotion.IsCreativeFlyEnabled = false;
        locomotion.LookAngles = Vector2.Zero;
        locomotion.LookOrder = Vector2.Zero;
        locomotion.TurnOrder = Vector2.Zero;
        locomotion.m_lookAutoLevelX = false;
        locomotion.m_lookAutoLevelY = false;
        InputHijackState.ResetGameplayInputs();
    }

    private static void BuildMotionPlatform(
        SubsystemTerrain terrain,
        Vector3 start,
        int floorY,
        int standingValue) {
        int centerX = Terrain.ToCell(start.X);
        int centerZ = Terrain.ToCell(start.Z);
        const int radius = 10;
        for (int x = centerX - radius; x <= centerX + radius; x++) {
            for (int z = centerZ - radius; z <= centerZ + radius; z++) {
                terrain.ChangeCell(x, floorY, z, standingValue);
                for (int y = floorY + 1; y <= floorY + 4; y++) {
                    terrain.ChangeCell(x, y, z, 0);
                }
            }
        }
    }

    private static Vector3 CreateDefaultMotionStart(SubsystemPlayers players) {
        Vector3 spawn = players.GlobalSpawnPosition;
        int chunkMinX = Terrain.ToCell(spawn.X) >> 4 << 4;
        int chunkMinZ = Terrain.ToCell(spawn.Z) >> 4 << 4;
        return new Vector3(
            chunkMinX + 8.5f,
            MathF.Floor(spawn.Y) + 0.01f,
            chunkMinZ + 8.5f);
    }

    private void RecordMotionEvent(
        PendingPlayerMotionAction pending,
        ActionEventPhase phase,
        string code,
        ComponentPlayer actor) {
        actionEventJournal.Record(
            Time.FrameIndex,
            pending.ActionId,
            nodeRole,
            AutomationCommandDomain.Player,
            MotionEventName(pending.Kind),
            phase,
            pending.PlayerIndex,
            MotionTarget(pending),
            code,
            MotionActionData(pending, actor),
            provenance: ActionProvenance.ProductionInput,
            productionEntry: ProductionPathEntries.PlayerMotion);
    }

    private static IReadOnlyDictionary<string, string> MotionActionData(
        PendingPlayerMotionAction pending,
        ComponentPlayer actor) =>
        new Dictionary<string, string> {
            ["kind"] = pending.Kind.ToString(),
            ["holdFrames"] = pending.HoldFrames.ToString(CultureInfo.InvariantCulture),
            ["input"] = MotionTarget(pending),
            ["baselinePosition"] = FormatVector(pending.BaselinePosition),
            ["actualPosition"] = FormatVector(actor.ComponentBody.Position),
            ["baselineLook"] = FormatVector(pending.BaselineLookAngles),
            ["actualLook"] = FormatVector(actor.ComponentLocomotion.LookAngles),
            ["baselineRotation"] = FormatQuaternion(pending.BaselineRotation),
            ["actualRotation"] = FormatQuaternion(actor.ComponentBody.Rotation),
            ["desiredCrouch"] = pending.DesiredCrouch.ToString(CultureInfo.InvariantCulture),
            ["actualCrouch"] = actor.ComponentBody.IsCrouching.ToString(CultureInfo.InvariantCulture)
        };

    private static string MotionEventName(PlayerMotionActionKind kind) =>
        kind switch {
            PlayerMotionActionKind.Move => "playerMove",
            PlayerMotionActionKind.Look => "playerLook",
            PlayerMotionActionKind.SetCrouch => "playerSetCrouch",
            _ => "playerMotion"
        };

    private static string MotionTarget(PendingPlayerMotionAction pending) =>
        pending.Kind switch {
            PlayerMotionActionKind.Move => FormatVector(pending.MoveInput),
            PlayerMotionActionKind.Look => FormatVector(pending.LookInput),
            PlayerMotionActionKind.SetCrouch => pending.DesiredCrouch.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };

    private static string FormatVector(Vector3 value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{value.X:R},{value.Y:R},{value.Z:R}");

    private static string FormatVector(Vector2 value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{value.X:R},{value.Y:R}");

    private static string FormatQuaternion(Quaternion value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{value.X:R},{value.Y:R},{value.Z:R},{value.W:R}");

    private static bool LookEffectObserved(
        PendingPlayerMotionAction pending,
        ComponentPlayer actor) {
        bool pitchObserved = MathF.Abs(pending.LookInput.Y) <= 0.0001f
            || Vector2.Distance(
                pending.BaselineLookAngles,
                actor.ComponentLocomotion.LookAngles) >= LookObservationDistance;
        bool yawObserved = MathF.Abs(pending.LookInput.X) <= 0.0001f
            || RotationDistance(
                pending.BaselineRotation,
                actor.ComponentBody.Rotation) >= RotationObservationAngle;
        return pitchObserved && yawObserved;
    }

    private static float RotationDistance(Quaternion first, Quaternion second) {
        float firstLength = MathF.Sqrt(
            first.X * first.X
            + first.Y * first.Y
            + first.Z * first.Z
            + first.W * first.W);
        float secondLength = MathF.Sqrt(
            second.X * second.X
            + second.Y * second.Y
            + second.Z * second.Z
            + second.W * second.W);
        if (firstLength <= float.Epsilon || secondLength <= float.Epsilon) {
            return float.PositiveInfinity;
        }
        float dot = MathF.Abs(
            (first.X * second.X
             + first.Y * second.Y
             + first.Z * second.Z
             + first.W * second.W)
            / (firstLength * secondLength));
        return 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f));
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second) {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return MathF.Sqrt(x * x + z * z);
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static Vector2 ToVector(Vector2State value) =>
        new((float)value.X, (float)value.Y);

    private static void ValidateHoldFrames(int holdFrames, string actionName) {
        if (holdFrames is < 1 or > 600) {
            throw new AutomationCommandException(
                "invalid_payload",
                $"{actionName} holdFrames must be between 1 and 600.");
        }
    }

    private static ActionReceipt QueuedReceipt(
        AutomationCommandContext context,
        int playerIndex) =>
        new(
            context.RequestId,
            true,
            Time.FrameIndex,
            playerIndex,
            "queued",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.PlayerMotion);
}
