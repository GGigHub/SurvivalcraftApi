using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;

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
                : ActionProvenance.ProductionRequest,
            productionEntry: name switch {
                "sleep" => ProductionPathEntries.SleepRequest,
                "wake" => ProductionPathEntries.WakeRequest,
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
