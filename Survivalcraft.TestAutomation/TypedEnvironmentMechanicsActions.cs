using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private SetEnvironmentFixtureResult SetEnvironmentFixture(
        SetEnvironmentFixtureCommand command) {
        EnsureServerFixtureRole("Environment fixtures");
        if (!double.IsFinite(command.TimeOfDayOffset)
            || Math.Abs(command.TimeOfDayOffset) > 1_000_000
            || !float.IsFinite(command.TimeOfYear)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Environment fixture values must be finite and bounded.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemGameInfo info = project.FindSubsystem<SubsystemGameInfo>(true);
        SubsystemWeather weather = project.FindSubsystem<SubsystemWeather>(true);
        info.WorldSettings.GameMode = command.CreativeMode
            ? GameMode.Creative
            : GameMode.Survival;
        info.TimeOfDay.TimeOfDayOffset = command.TimeOfDayOffset;
        info.WorldSettings.TimeOfYear = IntervalUtils.Normalize(command.TimeOfYear);
        ApplyWeatherFixture(
            info,
            weather,
            command.PrecipitationStarted,
            command.FogStarted);
        long timeRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Time,
            SubsystemTimePacket.EnvironmentStateId);
        long weatherRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId);
        QueueFixtureSynchronization(new SubsystemTimePacket(
            info.TotalElapsedGameTime,
            info.TimeOfDay.TimeOfDayOffset,
            timeRevision));
        QueueFixtureSynchronization(new SubsystemWeatherPacket(weather, weatherRevision));
        return new SetEnvironmentFixtureResult(
            info.TimeOfDay.TimeOfDayOffset,
            info.WorldSettings.TimeOfYear,
            weather.IsPrecipitationStarted,
            weather.IsFogStarted,
            timeRevision,
            weatherRevision);
    }

    private ActionReceipt QueueEnvironmentChange(
        AutomationCommandContext context,
        EnvironmentRequestChangeCommand command) {
        EnsureClientActionRole("Environment change actions");
        if (!Enum.IsDefined(command.Change)) {
            throw new AutomationCommandException("invalid_payload", "Unknown environment change kind.");
        }
        if (command.ForgeAuthoritativeState && !command.ExpectRejected) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Forged environment state must be an explicit rejection probe.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        SubsystemGameInfo info = project.FindSubsystem<SubsystemGameInfo>(true);
        SubsystemWeather weather = project.FindSubsystem<SubsystemWeather>(true);
        RecordEnvironmentAction(
            context.RequestId,
            command.Change,
            ActionEventPhase.Queued,
            command.PlayerIndex,
            command.ExpectRejected
                ? (command.ForgeAuthoritativeState ? "forged_state" : "rejection_probe")
                : "request");

        if (command.ForgeAuthoritativeState) {
            QueueForgedEnvironmentState(command, actor, info, weather);
        }
        else {
            switch (command.Change) {
                case EnvironmentChangeKind.TimeOfDay:
                    double offset = command.TimeOfDayOffset
                        ?? throw new AutomationCommandException(
                            "invalid_payload",
                            "Time-of-day changes require timeOfDayOffset.");
                    if (!double.IsFinite(offset)
                        || Math.Abs(offset - info.TimeOfDay.TimeOfDayOffset) > 1.0001) {
                        throw new AutomationCommandException(
                            "invalid_payload",
                            "Requested time-of-day offset is invalid or too far from current state.");
                    }
                    // ComponentGui applies the selection optimistically before raising its
                    // production request. Reproduce that behavior so rejected requests must
                    // prove that the server heals the requesting client.
                    info.TimeOfDay.TimeOfDayOffset = offset;
                    QueueProductionRequest(SubsystemTimePacket.CreateRequest(offset));
                    break;
                case EnvironmentChangeKind.PrecipitationStart:
                    weather.ManualPrecipitationStart();
                    QueueProductionRequest(new SubsystemWeatherPacket(2));
                    break;
                case EnvironmentChangeKind.PrecipitationEnd:
                    weather.ManualPrecipitationEnd();
                    QueueProductionRequest(new SubsystemWeatherPacket(1));
                    break;
                case EnvironmentChangeKind.FogStart:
                    weather.ManualFogStart();
                    QueueProductionRequest(new SubsystemWeatherPacket(4));
                    break;
                case EnvironmentChangeKind.FogEnd:
                    weather.ManualFogEnd();
                    QueueProductionRequest(new SubsystemWeatherPacket(3));
                    break;
                case EnvironmentChangeKind.Lightning:
                    Matrix eyeRotation = Matrix.CreateFromQuaternion(
                        actor.ComponentCreatureModel.EyeRotation);
                    QueueProductionRequest(new SubsystemSkyPacket(
                        actor.ComponentCreatureModel.EyePosition,
                        eyeRotation.Forward));
                    break;
            }
        }
        RecordEnvironmentAction(
            context.RequestId,
            command.Change,
            ActionEventPhase.Applied,
            command.PlayerIndex,
            command.ExpectRejected ? "rejection_probe_queued" : "production_packet_queued");
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            Provenance: command.ExpectRejected
                ? ActionProvenance.FaultInjection
                : ActionProvenance.ProductionRequest,
            ProductionEntry: EnvironmentProductionEntry(command.Change));
    }

    private EnvironmentRevisionBurstResult RunEnvironmentRevisionBurst(
        AutomationCommandContext context,
        RunEnvironmentRevisionBurstCommand command) {
        EnsureServerFixtureRole("Environment revision bursts");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemGameInfo info = project.FindSubsystem<SubsystemGameInfo>(true);
        SubsystemWeather weather = project.FindSubsystem<SubsystemWeather>(true);
        SubsystemWeatherPacket stale = new(weather);
        long staleRevision = stale.m_revision;
        RecordEnvironmentAction(
            context.RequestId,
            EnvironmentChangeKind.PrecipitationStart,
            ActionEventPhase.Queued,
            -1,
            "revision_burst");
        ApplyAuthoritativeWeatherSnapshot(info, weather, !command.FinalPrecipitationStarted);
        ApplyAuthoritativeWeatherSnapshot(info, weather, command.FinalPrecipitationStarted);
        long finalRevision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId);
        if (command.SendStaleSnapshot) {
            QueueFaultInjectionAfterSettle(stale);
        }
        RecordEnvironmentAction(
            context.RequestId,
            EnvironmentChangeKind.PrecipitationStart,
            ActionEventPhase.Applied,
            -1,
            "final_state_queued");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleSnapshot ? "stale_snapshot_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.EnvironmentWeather);
        return new EnvironmentRevisionBurstResult(
            receipt,
            command.FinalPrecipitationStarted,
            staleRevision,
            finalRevision);
    }

    private MechanicsFixture PrepareMechanicsFixture(PrepareMechanicsFixtureCommand command) {
        EnsureServerFixtureRole("Mechanics fixtures");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The mechanics fixture expects a connected remote player.");
        }
        ComponentSleep sleep = actor.ComponentSleep
            ?? throw new AutomationCommandException(
                "component_missing",
                "The player has no sleep component.");
        ComponentOnFire onFire = actor.Entity.FindComponent<ComponentOnFire>()
            ?? throw new AutomationCommandException(
                "component_missing",
                "The player has no on-fire component.");
        sleep.m_conditionsToSleep.Clear();
        actor.ComponentVitalStats.Sleep = 0f;
        actor.ComponentVitalStats.Wetness = 0f;
        sleep.WakeUp(true);
        onFire.Extinguish(true);
        long sleepRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Sleep,
            actor.Entity.Id);
        long fireRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Fire,
            actor.Entity.Id);
        QueueFixtureSynchronization(new SleepStatusPacket(
            sleep,
            sleep.m_allowManualWakeUp,
            true,
            revision: sleepRevision));
        QueueFixtureSynchronization(new EntityOnFirePacket(onFire, revision: fireRevision));
        return new MechanicsFixture(
            command.PlayerIndex,
            actor.Entity.Id,
            sleep.IsSleeping,
            onFire.IsOnFire);
    }

    private WorldMechanicsFixture PrepareWorldMechanicsFixture(
        PrepareWorldMechanicsFixtureCommand command) {
        EnsureServerFixtureRole("World mechanics fixtures");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        _ = ResolvePlayer(players, command.PlayerIndex, requireMainPlayer: false);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        int baseX = Terrain.ToCell(players.GlobalSpawnPosition.X) + 20;
        int baseY = Math.Clamp(Terrain.ToCell(players.GlobalSpawnPosition.Y) + 1, 8, 240);
        int baseZ = Terrain.ToCell(players.GlobalSpawnPosition.Z) + 20;
        TerrainCellCoordinate persistentFire = new(baseX, baseY, baseZ);
        TerrainCellCoordinate removedFire = new(baseX + 3, baseY, baseZ);
        TerrainCellCoordinate explosion = new(baseX + 12, baseY, baseZ);
        TerrainCellCoordinate explosionTarget = explosion;
        int fuelValue = Terrain.MakeBlockValue(OakWoodBlock.Index);
        int fireValue = Terrain.MakeBlockValue(FireBlock.Index);
        int explosionTargetValue = Terrain.MakeBlockValue(GlassBlock.Index);

        foreach (TerrainCellCoordinate fireCell in new[] { persistentFire, removedFire }) {
            terrain.ChangeCell(fireCell.X, fireCell.Y, fireCell.Z, 0);
            terrain.ChangeCell(fireCell.X, fireCell.Y - 1, fireCell.Z, fuelValue);
        }
        for (int x = explosion.X - 1; x <= explosion.X + 1; x++) {
            for (int y = explosion.Y - 1; y <= explosion.Y + 1; y++) {
                for (int z = explosion.Z - 1; z <= explosion.Z + 1; z++) {
                    terrain.ChangeCell(x, y, z, 0);
                }
            }
        }
        terrain.ChangeCell(
            explosionTarget.X,
            explosionTarget.Y,
            explosionTarget.Z,
            explosionTargetValue);
        terrain.TerrainUpdater.RequestSynchronousUpdate();
        return new WorldMechanicsFixture(
            persistentFire,
            removedFire,
            explosion,
            explosionTarget,
            fuelValue,
            fireValue,
            explosionTargetValue);
    }

    private SetPlayerFireFixtureResult SetPlayerFireFixture(
        SetPlayerFireFixtureCommand command) {
        EnsureServerFixtureRole("Player fire fixtures");
        if (command.EntityId <= 0
            || !float.IsFinite(command.Duration)
            || command.Duration is < 0f or > 3600f
            || !command.Extinguish && command.Duration <= 0f) {
            throw new AutomationCommandException("invalid_payload", "Player fire fixture is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentOnFire onFire = project.FindEntity(command.EntityId)
            ?.FindComponent<ComponentOnFire>()
            ?? throw new AutomationCommandException(
                "entity_not_found",
                $"Entity {command.EntityId} has no on-fire component.");
        if (command.Extinguish) {
            onFire.Extinguish(true);
        }
        else {
            onFire.SetOnFire(null, command.Duration, true);
        }
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Fire,
            command.EntityId);
        QueueFixtureSynchronization(new EntityOnFirePacket(onFire, revision: revision));
        return new SetPlayerFireFixtureResult(
            command.EntityId,
            onFire.IsOnFire,
            onFire.m_fireDuration,
            revision);
    }

    private ActionReceipt QueuePlayerSleep(
        AutomationCommandContext context,
        PlayerSleepCommand command) {
        EnsureClientActionRole("Player sleep actions");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        ComponentSleep sleep = actor.ComponentSleep
            ?? throw new AutomationCommandException(
                "component_missing",
                "The player has no sleep component.");
        int targetEntityId = command.TargetEntityId ?? sleep.Entity.Id;
        bool impersonatesAnotherEntity = targetEntityId != sleep.Entity.Id;
        if (command.ExpectRejected != impersonatesAnotherEntity) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Cross-player sleep/wake requests must be explicit rejection probes.");
        }
        RecordMechanicsAction(
            context.RequestId,
            command.Sleep ? "sleep" : "wake",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            targetEntityId,
            command.ExpectRejected ? "rejection_probe" : "request");
        if (command.Sleep) {
            SleepRequestPacket packet = new(sleep, command.AllowManualWakeup) {
                m_entityId = targetEntityId
            };
            if (command.ExpectRejected) {
                QueueFaultInjection(packet);
            }
            else {
                QueueProductionRequest(packet);
            }
        }
        else {
            WakeupRequestPacket packet = new(sleep) {
                m_entityId = targetEntityId
            };
            if (command.ExpectRejected) {
                QueueFaultInjection(packet);
            }
            else {
                QueueProductionRequest(packet);
            }
        }
        RecordMechanicsAction(
            context.RequestId,
            command.Sleep ? "sleep" : "wake",
            ActionEventPhase.Applied,
            command.PlayerIndex,
            targetEntityId,
            command.ExpectRejected ? "rejection_probe_queued" : "production_packet_queued");
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            Provenance: command.ExpectRejected
                ? ActionProvenance.FaultInjection
                : ActionProvenance.ProductionRequest,
            ProductionEntry: command.Sleep
                ? ProductionPathEntries.SleepRequest
                : ProductionPathEntries.WakeRequest);
    }

    private ActionReceipt QueuePlayerIgnite(
        AutomationCommandContext context,
        PlayerIgniteCommand command) {
        EnsureClientActionRole("Player ignite actions");
        if (command.TargetEntityId <= 0
            || !float.IsFinite(command.Duration)
            || command.Duration is <= 0f or > 3600f
            || !command.ExpectRejected) {
            throw new AutomationCommandException("invalid_payload", "Player ignite request is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        _ = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        long lifecycleRevision = EntityLifecycleTracker.GetAppliedRevision(
            project,
            command.TargetEntityId);
        EntityOnFirePacket forged = new() {
            m_entityId = command.TargetEntityId,
            m_isOnFire = true,
            m_duration = command.Duration,
            m_lifecycleRevision = lifecycleRevision,
            m_revision = long.MaxValue / 2
        };
        RecordMechanicsAction(
            context.RequestId,
            "ignite",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            command.TargetEntityId,
            "forged_authoritative_state");
        QueueFaultInjection(forged);
        RecordMechanicsAction(
            context.RequestId,
            "ignite",
            ActionEventPhase.Applied,
            command.PlayerIndex,
            command.TargetEntityId,
            "rejection_probe_queued");
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.FireState);
    }

    private MechanicsRevisionBurstResult RunMechanicsRevisionBurst(
        AutomationCommandContext context,
        RunMechanicsRevisionBurstCommand command) {
        EnsureServerFixtureRole("Mechanics revision bursts");
        if (command.EntityId <= 0
            || !float.IsFinite(command.IntermediateDuration)
            || !float.IsFinite(command.FinalDuration)
            || command.IntermediateDuration is <= 0f or > 3600f
            || command.FinalDuration is <= 0f or > 3600f) {
            throw new AutomationCommandException("invalid_payload", "Mechanics revision burst is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentOnFire onFire = project.FindEntity(command.EntityId)
            ?.FindComponent<ComponentOnFire>()
            ?? throw new AutomationCommandException(
                "entity_not_found",
                $"Entity {command.EntityId} has no on-fire component.");
        EntityOnFirePacket stale = new(onFire);
        long staleRevision = stale.m_revision;
        RecordMechanicsAction(
            context.RequestId,
            "fireRevisionBurst",
            ActionEventPhase.Queued,
            -1,
            command.EntityId,
            "queued");
        ApplyAuthoritativeFireSnapshot(onFire, command.IntermediateDuration);
        ApplyAuthoritativeFireSnapshot(onFire, command.FinalDuration);
        long finalRevision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Fire,
            command.EntityId);
        if (command.SendStaleSnapshot) {
            QueueFaultInjectionAfterSettle(stale);
        }
        RecordMechanicsAction(
            context.RequestId,
            "fireRevisionBurst",
            ActionEventPhase.Applied,
            -1,
            command.EntityId,
            "final_state_queued");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleSnapshot ? "stale_snapshot_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.FireState);
        return new MechanicsRevisionBurstResult(
            receipt,
            command.EntityId,
            command.FinalDuration,
            staleRevision,
            finalRevision);
    }

    private WorldFireLifecycleBurstResult RunWorldFireLifecycleBurst(
        AutomationCommandContext context,
        RunWorldFireLifecycleBurstCommand command) {
        EnsureServerFixtureRole("World fire lifecycle bursts");
        ValidateTerrainCoordinate(command.PersistentFireCell);
        ValidateTerrainCoordinate(command.RemovedFireCell);
        if (command.PersistentFireCell == command.RemovedFireCell
            || !float.IsFinite(command.Expandability)
            || command.Expandability is < 0f or > 16f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "World fire lifecycle burst is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        Point3 persistent = ToPoint(command.PersistentFireCell);
        Point3 removed = ToPoint(command.RemovedFireCell);
        RecordMechanicsAction(
            context.RequestId,
            "worldFireLifecycle",
            ActionEventPhase.Queued,
            -1,
            -1,
            "revision_burst");

        int fireValue = Terrain.MakeBlockValue(FireBlock.Index);
        terrain.ChangeCell(persistent.X, persistent.Y, persistent.Z, fireValue);
        terrain.ChangeCell(removed.X, removed.Y, removed.Z, fireValue);
        SubsystemFireBlockBehavior fireBehavior =
            project.FindSubsystem<SubsystemFireBlockBehavior>(true);
        if (fireBehavior.m_fireData.TryGetValue(
                persistent,
                out SubsystemFireBlockBehavior.FireData? persistentData)
            && persistentData != null) {
            persistentData.Time0 = 3600f;
            persistentData.Time1 = 3600f;
            persistentData.Time2 = 3600f;
            persistentData.Time3 = 3600f;
            persistentData.Time5 = 3600f;
        }
        long persistentRevision = WorldFireStateTracker.GetAuthoritativeRevision(
            project,
            persistent);
        long staleRevision = WorldFireStateTracker.GetAuthoritativeRevision(project, removed);
        FireBlockAddPacket stale = new() {
            x = removed.X,
            y = removed.Y,
            z = removed.Z,
            m_expandability = command.Expandability,
            m_revision = staleRevision
        };
        terrain.ChangeCell(removed.X, removed.Y, removed.Z, 0);
        long removedRevision = WorldFireStateTracker.GetAuthoritativeRevision(project, removed);
        if (persistentRevision <= 0 || staleRevision <= 0 || removedRevision <= staleRevision) {
            throw new AutomationCommandException(
                "invalid_state",
                "World fire hooks did not publish a monotonic lifecycle.");
        }
        if (command.SendStaleAdd) {
            QueueFaultInjectionAfterSettle(stale);
        }
        RecordMechanicsAction(
            context.RequestId,
            "worldFireLifecycle",
            ActionEventPhase.Applied,
            -1,
            -1,
            command.SendStaleAdd ? "stale_add_queued" : "final_state_queued");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleAdd ? "stale_add_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.WorldFireLifecycle);
        return new WorldFireLifecycleBurstResult(
            receipt,
            command.PersistentFireCell,
            command.RemovedFireCell,
            persistentRevision,
            staleRevision,
            removedRevision);
    }

    private TriggerExplosionResult TriggerExplosion(
        AutomationCommandContext context,
        TriggerExplosionCommand command) {
        EnsureServerFixtureRole("Explosion actions");
        ValidateTerrainCoordinate(command.Cell);
        if (!float.IsFinite(command.Pressure)
            || command.Pressure is <= 0f or > 100_000_000f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Explosion pressure must be finite, positive and bounded.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemExplosions explosions = project.FindSubsystem<SubsystemExplosions>(true);
        long expectedEventId = checked(explosions.NetworkEventId + 1);
        RecordMechanicsAction(
            context.RequestId,
            "explosion",
            ActionEventPhase.Queued,
            -1,
            -1,
            "production_event");
        explosions.AddExplosion(
            command.Cell.X,
            command.Cell.Y,
            command.Cell.Z,
            command.Pressure,
            command.Incendiary,
            noExplosionSound: false);
        RecordMechanicsAction(
            context.RequestId,
            "explosion",
            ActionEventPhase.Applied,
            -1,
            -1,
            "production_event_queued");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            "queued",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.ExplosionEvent);
        return new TriggerExplosionResult(
            receipt,
            command.Cell,
            expectedEventId,
            command.Pressure,
            command.Incendiary);
    }

    private ActionReceipt ProbeExplosionAuthority(
        AutomationCommandContext context,
        ProbeExplosionAuthorityCommand command) {
        EnsureClientActionRole("Explosion authority probes");
        ValidateTerrainCoordinate(command.Cell);
        if (!float.IsFinite(command.Pressure)
            || command.Pressure is <= 0f or > 100_000_000f
            || command.ForgedEventId <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Explosion authority probe values must be finite, positive and bounded.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        SubsystemExplosions explosions = project.FindSubsystem<SubsystemExplosions>(true);
        int queuedBefore = explosions.m_queuedExplosions.Count;
        RecordMechanicsAction(
            context.RequestId,
            "explosionAuthority",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            actor.Entity.Id,
            "local_and_packet_forgery");

        explosions.AddExplosion(
            command.Cell.X,
            command.Cell.Y,
            command.Cell.Z,
            command.Pressure,
            isIncendiary: false,
            noExplosionSound: false);
        if (explosions.m_queuedExplosions.Count != queuedBefore) {
            throw new AutomationCommandException(
                "authority_violation",
                "Client-side AddExplosion entered the authoritative simulation queue.");
        }

        ExplosionCellPacket cells = new() {
            m_eventId = command.ForgedEventId,
            m_chunkIndex = 0,
            m_chunkCount = 1
        };
        cells.m_cells.Add(new ExplosionCellPacket.CellData {
            Point = ToPoint(command.Cell),
            Intensity = 0.5f
        });
        QueueFaultInjection(cells);
        QueueFaultInjection(new ExplosionSoundPacket(
            command.ForgedEventId,
            new Vector3(
                command.Cell.X + 0.5f,
                command.Cell.Y + 0.5f,
                command.Cell.Z + 0.5f),
            command.Pressure,
            0f));
        RecordMechanicsAction(
            context.RequestId,
            "explosionAuthority",
            ActionEventPhase.Applied,
            command.PlayerIndex,
            actor.Entity.Id,
            "rejection_probe_queued");
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            "rejection_probe_queued",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.ExplosionEvent);
    }

    private ActionReceipt QueueForgedWorldFireState(
        AutomationCommandContext context,
        ForgeWorldFireStateCommand command) {
        EnsureClientActionRole("Forged world fire state probes");
        ValidateTerrainCoordinate(command.Cell);
        if (!command.ExpectRejected
            || command.Revision <= 0
            || !float.IsFinite(command.Expandability)
            || command.Expandability is < 0f or > 16f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "World fire state injection must be an explicit bounded rejection probe.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        RecordMechanicsAction(
            context.RequestId,
            "worldFireForgery",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            actor.Entity.Id,
            "forged_authoritative_state");
        if (command.Present) {
            QueueFaultInjection(new FireBlockAddPacket {
                x = command.Cell.X,
                y = command.Cell.Y,
                z = command.Cell.Z,
                m_expandability = command.Expandability,
                m_revision = command.Revision
            });
        }
        else {
            QueueFaultInjection(new FireBlockRemovePacket {
                x = command.Cell.X,
                y = command.Cell.Y,
                z = command.Cell.Z,
                m_revision = command.Revision
            });
        }
        RecordMechanicsAction(
            context.RequestId,
            "worldFireForgery",
            ActionEventPhase.Applied,
            command.PlayerIndex,
            actor.Entity.Id,
            "rejection_probe_queued");
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            "rejection_probe_queued",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.WorldFireLifecycle);
    }

    private static void QueueForgedEnvironmentState(
        EnvironmentRequestChangeCommand command,
        ComponentPlayer actor,
        SubsystemGameInfo info,
        SubsystemWeather weather) {
        long forgedRevision = long.MaxValue / 2;
        switch (command.Change) {
            case EnvironmentChangeKind.TimeOfDay:
                QueueFaultInjection(new SubsystemTimePacket(
                    info.TotalElapsedGameTime + 10_000,
                    command.TimeOfDayOffset ?? info.TimeOfDay.TimeOfDayOffset + 0.5,
                    forgedRevision));
                break;
            case EnvironmentChangeKind.Lightning:
                QueueFaultInjection(new SubsystemSkyPacket(
                    actor.ComponentCreatureModel.EyePosition,
                    forgedRevision));
                break;
            default:
                SubsystemWeatherPacket packet = new(weather, forgedRevision);
                packet.m_precipitationStartTime = info.TotalElapsedGameTime;
                packet.m_precipitationEndTime = double.PositiveInfinity;
                packet.m_fogStartTime = info.TotalElapsedGameTime;
                packet.m_fogEndTime = double.PositiveInfinity;
                QueueFaultInjection(packet);
                break;
        }
    }

    private static void ApplyWeatherFixture(
        SubsystemGameInfo info,
        SubsystemWeather weather,
        bool precipitationStarted,
        bool fogStarted) {
        double now = info.TotalElapsedGameTime;
        weather.m_precipitationStartTime = precipitationStarted ? now : now + 600;
        weather.m_precipitationEndTime = precipitationStarted
            ? double.PositiveInfinity
            : now + 660;
        weather.m_precipitationRampTime = 1f;
        weather.m_lightningIntensity = precipitationStarted ? 1f : 0f;
        weather.m_fogStartTime = fogStarted ? now : now + 600;
        weather.m_fogEndTime = fogStarted ? double.PositiveInfinity : now + 660;
        weather.m_fogRampTime = 1f;
        weather.FogProgress = 0f;
    }

    private static void ApplyAuthoritativeWeatherSnapshot(
        SubsystemGameInfo info,
        SubsystemWeather weather,
        bool precipitationStarted) {
        ApplyWeatherFixture(info, weather, precipitationStarted, weather.IsFogStarted);
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            weather.Project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId);
        QueueAuthoritativeState(new SubsystemWeatherPacket(weather, revision));
    }

    private static void ApplyAuthoritativeFireSnapshot(
        ComponentOnFire onFire,
        float duration) {
        onFire.Extinguish(true);
        onFire.SetOnFire(null, duration, true);
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            onFire.Entity.Project,
            NetworkStateRevisionDomain.Fire,
            onFire.Entity.Id);
        QueueAuthoritativeState(new EntityOnFirePacket(onFire, revision: revision));
    }

    private void RecordEnvironmentAction(
        string actionId,
        EnvironmentChangeKind change,
        ActionEventPhase phase,
        int playerIndex,
        string code) {
        bool fault = code.Contains("forged", StringComparison.Ordinal)
            || code.Contains("rejection_probe", StringComparison.Ordinal)
            || code.Contains("revision_burst", StringComparison.Ordinal)
            || code.Contains("final_state", StringComparison.Ordinal);
        actionEventJournal.Record(
            Time.FrameIndex,
            actionId,
            nodeRole,
            AutomationCommandDomain.Environment,
            change.ToString(),
            phase,
            playerIndex >= 0 ? playerIndex : null,
            "environment",
            code,
            provenance: fault
                ? ActionProvenance.FaultInjection
                : ActionProvenance.ProductionRequest,
            productionEntry: EnvironmentProductionEntry(change));
    }

    private void RecordMechanicsAction(
        string actionId,
        string name,
        ActionEventPhase phase,
        int playerIndex,
        int entityId,
        string code) {
        bool fault = string.Equals(name, "ignite", StringComparison.Ordinal)
            || string.Equals(name, "fireRevisionBurst", StringComparison.Ordinal)
            || string.Equals(name, "worldFireLifecycle", StringComparison.Ordinal)
            || string.Equals(name, "worldFireForgery", StringComparison.Ordinal)
            || code.Contains("rejection_probe", StringComparison.Ordinal)
            || code.Contains("forged", StringComparison.Ordinal);
        actionEventJournal.Record(
            Time.FrameIndex,
            actionId,
            nodeRole,
            AutomationCommandDomain.Mechanics,
            name,
            phase,
            playerIndex >= 0 ? playerIndex : null,
            entityId.ToString(CultureInfo.InvariantCulture),
            code,
            provenance: fault
                ? ActionProvenance.FaultInjection
                : string.Equals(name, "explosion", StringComparison.Ordinal)
                    ? ActionProvenance.ProductionInput
                    : ActionProvenance.ProductionRequest,
            productionEntry: name switch {
                "sleep" => ProductionPathEntries.SleepRequest,
                "wake" => ProductionPathEntries.WakeRequest,
                "worldFireLifecycle" or "worldFireForgery" =>
                    ProductionPathEntries.WorldFireLifecycle,
                "explosion" => ProductionPathEntries.ExplosionEvent,
                _ => ProductionPathEntries.FireState
            });
    }

    private static string EnvironmentProductionEntry(EnvironmentChangeKind change) =>
        change switch {
            EnvironmentChangeKind.TimeOfDay => ProductionPathEntries.EnvironmentTime,
            EnvironmentChangeKind.Lightning => ProductionPathEntries.EnvironmentSky,
            _ => ProductionPathEntries.EnvironmentWeather
        };
}
