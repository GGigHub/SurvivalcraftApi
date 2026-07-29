using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private PendingHealthAction? pendingHealthAction;

    private enum HealthActionKind {
        Hit,
        Respawn
    }

    private sealed record PendingHealthAction(
        string ActionId,
        HealthActionKind Kind,
        int PlayerIndex,
        int TargetEntityId,
        int DeadlineFrame);

    private HealthFixture PrepareHealthFixture(PrepareHealthFixtureCommand command) {
        EnsureServerFixtureRole("Health fixtures");
        if (!double.IsFinite(command.TargetHealth)
            || command.TargetHealth is <= 0 or > 1) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Target health must be finite and in the interval (0, 1].");
        }
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The health fixture expects a connected remote player.");
        }

        Vector3 playerPosition = actor.ComponentBody.Position;
        playerPosition = new Vector3(
            MathF.Floor(playerPosition.X) + 0.5f,
            MathF.Floor(playerPosition.Y) + 0.05f,
            MathF.Floor(playerPosition.Z) + 0.5f);
        PrepareHealthActor(actor, playerPosition);
        Vector3 targetPosition = playerPosition + new Vector3(0f, 0.1f, 1.5f);
        Entity target = CreateStableEntity(project, "Cow", targetPosition);
        target.FindComponent<ComponentBody>(true).BodyCollidable = true;
        ComponentHealth targetHealth = target.FindComponent<ComponentHealth>(true);
        targetHealth.IsInvulnerable = false;
        targetHealth.m_regenerateLifeEnabled = false;
        targetHealth.Health = (float)command.TargetHealth;
        targetHealth.m_lastHealth = targetHealth.Health;
        targetHealth.CauseOfDeath = null;
        targetHealth.DeathTime = null;
        targetHealth.CorpseDuration = 0f;
        project.AddEntity(target);

        int weaponValue = Terrain.MakeBlockValue(IronMacheteBlock.Index);
        IInventory inventory = actor.ComponentMiner.Inventory;
        SetInventorySlot(inventory, 0, weaponValue, 1);
        inventory.ActiveSlotIndex = 0;
        actor.PlayerData.Level = Math.Max(actor.PlayerData.Level, 10f);
        NetworkManager.Queue(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new HealthFixture(
            command.PlayerIndex,
            actor.Entity.Id,
            target.Id,
            ToVector(playerPosition),
            ToVector(targetPosition),
            weaponValue,
            command.TargetHealth);
    }

    private ControlAck ApplyHealthFixture(ApplyHealthFixtureCommand command) {
        HealthFixture fixture = command.Fixture
            ?? throw new AutomationCommandException("invalid_payload", "Fixture payload is required.");
        EnsureClientActionRole("Health actor fixtures");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            fixture.PlayerIndex,
            requireMainPlayer: true);
        if (actor.Entity.Id != fixture.PlayerEntityId) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "The health fixture player entity does not match this client.");
        }
        PrepareHealthActor(actor, ToVector(fixture.PlayerPosition));
        actor.PlayerData.Level = Math.Max(actor.PlayerData.Level, 10f);
        SetInventorySlot(actor.ComponentMiner.Inventory, 0, fixture.WeaponValue, 1);
        actor.ComponentMiner.Inventory.ActiveSlotIndex = 0;
        NetworkManager.Queue(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new ControlAck(true, $"Applied health fixture for player {fixture.PlayerIndex}.");
    }

    private SetHealthFixtureResult SetHealthFixture(SetHealthFixtureCommand command) {
        EnsureServerFixtureRole("Health state fixtures");
        if (command.EntityId <= 0
            || !double.IsFinite(command.Health)
            || command.Health is < 0 or > 1) {
            throw new AutomationCommandException("invalid_payload", "Health fixture state is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentHealth health = project.FindEntity(command.EntityId)
            ?.FindComponent<ComponentHealth>()
            ?? throw new AutomationCommandException(
                "entity_not_found",
                $"Entity {command.EntityId} has no health component.");
        health.IsInvulnerable = false;
        health.m_regenerateLifeEnabled = false;
        health.CauseOfDeath = null;
        health.DeathTime = null;
        health.Health = command.Kill ? 1f : (float)command.Health;
        health.m_lastHealth = health.Health;
        if (command.Kill) {
            health.Injure(2f, null, true, command.Cause);
        }
        else {
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                project,
                NetworkStateRevisionDomain.Health,
                command.EntityId);
            NetworkManager.Queue(new HealthSyncPacket(health, revision));
        }
        long finalRevision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Health,
            command.EntityId);
        return new SetHealthFixtureResult(
            command.EntityId,
            health.Health,
            health.Health <= 0f,
            finalRevision);
    }

    private ActionReceipt QueuePlayerHit(
        AutomationCommandContext context,
        PlayerHitCommand command) {
        EnsureClientActionRole("Player hit actions");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        ComponentBody target = project.FindEntity(command.TargetEntityId)
            ?.FindComponent<ComponentBody>()
            ?? throw new AutomationCommandException(
                "target_not_found",
                $"Target entity {command.TargetEntityId} has no body on this client.");
        Vector3 eye = actor.ComponentCreatureModel.EyePosition;
        Vector3 targetPoint = target.BoundingBox.Center();
        Vector3 delta = targetPoint - eye;
        if (command.ExpectRejected) {
            Vector3 direction = delta.LengthSquared() > 1E-06f
                ? Vector3.Normalize(delta)
                : Vector3.UnitZ;
            long rejectedTick = Time.FrameIndex;
            RecordHealthAction(
                context.RequestId,
                "hit",
                ActionEventPhase.Queued,
                command.PlayerIndex,
                command.TargetEntityId,
                "queued_expected_rejection");
            NetworkManager.Queue(new PlayerHitPacket(
                actor,
                target,
                targetPoint + new Vector3(32f, 0f, 0f),
                direction));
            RecordHealthAction(
                context.RequestId,
                "hit",
                ActionEventPhase.Observed,
                command.PlayerIndex,
                command.TargetEntityId,
                "forged_hit_packet_sent_expected_rejection");
            return new ActionReceipt(
                context.RequestId,
                true,
                rejectedTick,
                command.PlayerIndex,
                "queued_expected_rejection");
        }
        float distance = delta.Length();
        if (!float.IsFinite(distance) || distance is < 0.05f or > 2.25f) {
            throw new AutomationCommandException(
                "target_not_reachable",
                $"Target entity {command.TargetEntityId} is {distance:R} blocks from the actor eye.");
        }
        Ray3 ray = new(eye, Vector3.Normalize(delta));
        BodyRaycastResult? hit = actor.ComponentMiner.Raycast<BodyRaycastResult>(
            ray,
            RaycastMode.Interaction,
            reach: 2f);
        if (!hit.HasValue || hit.Value.ComponentBody.Entity.Id != command.TargetEntityId) {
            throw new AutomationCommandException(
                "target_not_visible",
                "The production melee ray did not resolve the requested target.");
        }

        long tick = Time.FrameIndex;
        RecordHealthAction(
            context.RequestId,
            "hit",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            command.TargetEntityId,
            "queued");
        pendingHealthAction = new PendingHealthAction(
            context.RequestId,
            HealthActionKind.Hit,
            command.PlayerIndex,
            command.TargetEntityId,
            checked(Time.FrameIndex + 180));
        InputHijackState.QueueHitRay(ray);
        ScheduleHealthActionTimeout(pendingHealthAction);
        return new ActionReceipt(context.RequestId, true, tick, command.PlayerIndex, "queued");
    }

    private ActionReceipt QueuePlayerRespawn(
        AutomationCommandContext context,
        PlayerRequestRespawnCommand command) {
        EnsureClientActionRole("Player respawn actions");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: !command.ExpectRejected,
            requireReady: !command.ExpectRejected);
        if (actor.ComponentHealth.Health > 0f) {
            throw new AutomationCommandException(
                "player_not_dead",
                "A respawn request is only valid after authoritative player death.");
        }
        if (command.ExpectRejected) {
            if (actor.PlayerData.IsMainPlayer) {
                throw new AutomationCommandException(
                    "invalid_payload",
                    "An expected-rejection respawn must impersonate another player.");
            }
            long rejectedTick = Time.FrameIndex;
            RecordHealthAction(
                context.RequestId,
                "requestRespawn",
                ActionEventPhase.Queued,
                command.PlayerIndex,
                actor.Entity.Id,
                "queued_expected_rejection");
            NetworkManager.Queue(new PlayerRestartPacket(actor.PlayerData));
            RecordHealthAction(
                context.RequestId,
                "requestRespawn",
                ActionEventPhase.Observed,
                command.PlayerIndex,
                actor.Entity.Id,
                "forged_respawn_packet_sent_expected_rejection");
            return new ActionReceipt(
                context.RequestId,
                true,
                rejectedTick,
                command.PlayerIndex,
                "queued_expected_rejection");
        }
        long tick = Time.FrameIndex;
        RecordHealthAction(
            context.RequestId,
            "requestRespawn",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            actor.Entity.Id,
            "queued");
        pendingHealthAction = new PendingHealthAction(
            context.RequestId,
            HealthActionKind.Respawn,
            command.PlayerIndex,
            actor.Entity.Id,
            checked(Time.FrameIndex + 600));
        actor.PlayerData.readyToRestart = true;
        ScheduleHealthActionTimeout(pendingHealthAction);
        return new ActionReceipt(context.RequestId, true, tick, command.PlayerIndex, "queued");
    }

    private HealthRevisionBurstResult RunHealthRevisionBurst(
        AutomationCommandContext context,
        RunHealthRevisionBurstCommand command) {
        EnsureServerFixtureRole("Health revision bursts");
        if (command.EntityId <= 0
            || !double.IsFinite(command.IntermediateHealth)
            || !double.IsFinite(command.FinalHealth)
            || command.IntermediateHealth is < 0 or > 1
            || command.FinalHealth is < 0 or > 1) {
            throw new AutomationCommandException("invalid_payload", "Health revision burst is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentHealth health = project.FindEntity(command.EntityId)
            ?.FindComponent<ComponentHealth>()
            ?? throw new AutomationCommandException(
                "entity_not_found",
                $"Entity {command.EntityId} has no health component.");
        HealthSyncPacket stale = new(health);
        long staleRevision = stale.m_revision;
        RecordHealthAction(
            context.RequestId,
            "revisionBurst",
            ActionEventPhase.Queued,
            -1,
            command.EntityId,
            "queued");
        ApplyAuthoritativeHealthSnapshot(health, (float)command.IntermediateHealth);
        ApplyAuthoritativeHealthSnapshot(health, (float)command.FinalHealth);
        long finalRevision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Health,
            command.EntityId);
        if (command.SendStaleSnapshot) {
            NetworkManager.Queue(stale);
        }
        RecordHealthAction(
            context.RequestId,
            "revisionBurst",
            ActionEventPhase.Applied,
            -1,
            command.EntityId,
            "final_state_queued");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleSnapshot ? "stale_snapshot_queued" : "applied");
        return new HealthRevisionBurstResult(
            receipt,
            command.EntityId,
            command.FinalHealth,
            staleRevision,
            finalRevision);
    }

    private static void ApplyAuthoritativeHealthSnapshot(
        ComponentHealth health,
        float value) {
        health.Health = value;
        health.m_lastHealth = value;
        health.CauseOfDeath = null;
        health.DeathTime = null;
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            health.Entity.Project,
            NetworkStateRevisionDomain.Health,
            health.Entity.Id);
        NetworkManager.Queue(new HealthSyncPacket(health, revision));
    }

    private void HandleTypedHitEvent(
        ComponentPlayer player,
        ComponentBody body,
        Vector3 hitPoint,
        Vector3 direction) {
        PendingHealthAction? pending = pendingHealthAction;
        if (pending?.Kind != HealthActionKind.Hit
            || player.PlayerData.PlayerIndex != pending.PlayerIndex
            || body?.Entity?.Id != pending.TargetEntityId) {
            return;
        }
        RecordHealthAction(
            pending.ActionId,
            "hit",
            ActionEventPhase.Applied,
            pending.PlayerIndex,
            pending.TargetEntityId,
            "production_hit_input_observed");
        pendingHealthAction = null;
    }

    private void HandleTypedPlayerRestart(PlayerData playerData) {
        PendingHealthAction? pending = pendingHealthAction;
        if (pending?.Kind != HealthActionKind.Respawn
            || playerData.PlayerIndex != pending.PlayerIndex) {
            return;
        }
        RecordHealthAction(
            pending.ActionId,
            "requestRespawn",
            ActionEventPhase.Applied,
            pending.PlayerIndex,
            pending.TargetEntityId,
            "production_respawn_request_observed");
        pendingHealthAction = null;
    }

    private void ScheduleHealthActionTimeout(PendingHealthAction pending) {
        Time.QueueFrameIndexDelayedExecution(pending.DeadlineFrame, () => {
            if (pendingHealthAction?.ActionId != pending.ActionId) {
                return;
            }
            RecordHealthAction(
                pending.ActionId,
                pending.Kind.ToString(),
                ActionEventPhase.Rejected,
                pending.PlayerIndex,
                pending.TargetEntityId,
                "production_input_not_observed");
            pendingHealthAction = null;
        });
    }

    private void RecordHealthAction(
        string actionId,
        string name,
        ActionEventPhase phase,
        int playerIndex,
        int targetEntityId,
        string code) {
        actionEventJournal.Record(
            Time.FrameIndex,
            actionId,
            nodeRole,
            AutomationCommandDomain.Health,
            name,
            phase,
            playerIndex >= 0 ? playerIndex : null,
            targetEntityId.ToString(CultureInfo.InvariantCulture),
            code);
    }

    private static void PrepareHealthActor(ComponentPlayer actor, Vector3 position) {
        PrepareInventoryActor(actor, position);
        actor.ComponentHealth.IsInvulnerable = false;
        actor.ComponentHealth.m_regenerateLifeEnabled = false;
        actor.ComponentHealth.Health = 1f;
        actor.ComponentHealth.m_lastHealth = 1f;
        actor.ComponentHealth.CauseOfDeath = null;
        actor.ComponentHealth.DeathTime = null;
        actor.ComponentMiner.m_lastHitTime = -1000;
    }
}
