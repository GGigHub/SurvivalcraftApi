using System.Globalization;
using Engine;
using Engine.Media;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private SurvivalStateFixture? survivalStateFixture;

    private SurvivalStateFixture PrepareSurvivalStateFixture(
        PrepareSurvivalStateFixtureCommand command) {
        EnsureSurvivalServerRole("Survival-state fixtures");
        if (!SkinPacketBase.TryValidateSkinName(command.SkinName, out string skinName)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "The fixture skin name must be a safe custom .scskin file name.");
        }
        if (survivalStateFixture != null) {
            throw new AutomationCommandException(
                "invalid_state",
                "A survival-state fixture is already active in this project.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The survival-state fixture expects a connected remote player.");
        }

        ComponentClothing clothing = actor.Entity.FindComponent<ComponentClothing>(true);
        clothing.EnableDressLimit = false;
        (ClothingSlot clothingSlot, int clothingValue) = FindFixtureClothing();
        byte[] skinData = CreateFixtureSkinData();
        if (!SkinPacketBase.SaveSkin(skinName, skinData)) {
            throw new AutomationCommandException(
                "fixture_failed",
                "The deterministic custom skin could not be validated and saved.");
        }
        actor.PlayerData.CharacterSkinName = skinName;
        QueueFixtureSynchronization(new PlayerModifyPacket(actor.PlayerData));

        Vector3 origin = actor.ComponentBody.Position + new Vector3(8f, 2f, 0f);
        Entity chase = AddStableBehaviorEntity<ComponentChaseBehavior>(
            project, "Cow", origin + new Vector3(0f, 0f, 0f));
        Entity dig = AddStableBehaviorEntity<ComponentDigInMudBehavior>(
            project, "Ray_Yellow", origin + new Vector3(2f, 0f, 0f));
        Entity eat = AddStableBehaviorEntity<ComponentEatPickableBehavior>(
            project, "Cow", origin + new Vector3(4f, 0f, 0f));
        Entity fish = AddStableBehaviorEntity<ComponentFishOutOfWaterBehavior>(
            project, "Bass_Freshwater", origin + new Vector3(6f, 0f, 0f));
        Entity look = AddStableBehaviorEntity<ComponentLookAroundBehavior>(
            project, "Duck", origin + new Vector3(8f, 0f, 0f));
        Entity feed = AddStableBehaviorEntity<ComponentRandomFeedBehavior>(
            project, "Cow", origin + new Vector3(10f, 0f, 0f));
        Entity peck = AddStableBehaviorEntity<ComponentRandomPeckBehavior>(
            project, "Duck", origin + new Vector3(12f, 0f, 0f));
        Entity stubborn = AddStableBehaviorEntity<ComponentStubbornSteedBehavior>(
            project, "Donkey_Saddled", origin + new Vector3(14f, 0f, 0f));
        Entity sound = AddStableBehaviorEntity<ComponentCreatureSounds>(
            project, "Duck", origin + new Vector3(16f, 0f, 0f));

        survivalStateFixture = new SurvivalStateFixture(
            command.PlayerIndex,
            actor.Entity.Id,
            clothing.Id,
            clothingSlot.StableId,
            clothingValue,
            skinName,
            new CreatureBehaviorFixture(
                chase.Id,
                dig.Id,
                eat.Id,
                fish.Id,
                look.Id,
                feed.Id,
                peck.Id,
                stubborn.Id,
                sound.Id));
        return survivalStateFixture;
    }

    private CreatureBehaviorStateResult SetCreatureBehaviorState(
        AutomationCommandContext context,
        SetCreatureBehaviorStateCommand command) {
        EnsureSurvivalServerRole("Creature behavior actions");
        ValidateBehaviorFixture(command.Fixture);
        if (!float.IsFinite(command.HeadShakeOrder)
            || command.HeadShakeOrder is < 0f or > 8f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Head-shake order must be finite and between 0 and 8.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "creatureBehaviorState",
            ActionEventPhase.Queued,
            target: string.Join(',', command.Fixture.EntityIds),
            code: "queued",
            provenance: ActionProvenance.ProductionInput,
            productionEntry: ProductionPathEntries.CreatureBehaviorState);

        ResolveFixtureComponent<ComponentChaseBehavior>(project, command.Fixture.ChaseEntityId)
            .IsAttack = command.Enabled;
        ResolveFixtureComponent<ComponentDigInMudBehavior>(project, command.Fixture.DigInMudEntityId)
            .IsDigIn = command.Enabled;
        ResolveFixtureComponent<ComponentEatPickableBehavior>(project, command.Fixture.EatPickableEntityId)
            .IsFeed = command.Enabled;
        ResolveFixtureComponent<ComponentFishOutOfWaterBehavior>(project, command.Fixture.FishOutOfWaterEntityId)
            .IsBend = command.Enabled;
        ResolveFixtureComponent<ComponentLookAroundBehavior>(project, command.Fixture.LookAroundEntityId)
            .IsLookRandom = command.Enabled;
        ResolveFixtureComponent<ComponentRandomFeedBehavior>(project, command.Fixture.RandomFeedEntityId)
            .IsFeed = command.Enabled;
        ResolveFixtureComponent<ComponentRandomPeckBehavior>(project, command.Fixture.RandomPeckEntityId)
            .IsFeed = command.Enabled;
        ResolveFixtureComponent<ComponentStubbornSteedBehavior>(project, command.Fixture.StubbornSteedEntityId)
            .SetHeadShakeOrder(command.Enabled ? command.HeadShakeOrder : 0f);

        long[] revisions = GetBehaviorRevisions(project, command.Fixture);
        long minimumRevision = revisions.Min();
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "creatureBehaviorState",
            ActionEventPhase.Applied,
            target: string.Join(',', command.Fixture.EntityIds),
            code: command.Enabled ? "enabled" : "disabled",
            data: new Dictionary<string, string> {
                ["minimumRevision"] = minimumRevision.ToString(CultureInfo.InvariantCulture),
                ["headShakeOrder"] = command.HeadShakeOrder.ToString("R", CultureInfo.InvariantCulture)
            },
            provenance: ActionProvenance.ProductionInput,
            productionEntry: ProductionPathEntries.CreatureBehaviorState);
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            "applied",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.CreatureBehaviorState);
        return new CreatureBehaviorStateResult(receipt, minimumRevision, command.HeadShakeOrder);
    }

    private static ControlAck QuiesceSurvivalStateFixture(
        QuiesceSurvivalStateFixtureCommand command) {
        ArgumentNullException.ThrowIfNull(command.Fixture);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemUpdate subsystemUpdate = project.FindSubsystem<SubsystemUpdate>(true);
        foreach (int entityId in command.Fixture.EntityIds) {
            Entity entity = project.FindEntity(entityId)
                ?? throw new AutomationCommandException(
                    "fixture_not_ready",
                    $"Survival-state entity {entityId} has not replicated to this node.");
            foreach (ComponentBehavior behavior in entity.FindComponents<ComponentBehavior>()) {
                if (behavior is IUpdateable updateable) {
                    subsystemUpdate.RemoveUpdateable(updateable);
                }
            }
            if (entity.FindComponent<ComponentCreatureModel>() is IUpdateable modelUpdateable) {
                subsystemUpdate.RemoveUpdateable(modelUpdateable);
            }
            ComponentCreatureSounds? sounds = entity.FindComponent<ComponentCreatureSounds>();
            if (sounds != null) {
                DisableFixtureSounds(sounds);
            }
        }
        return new ControlAck(true, "Survival-state fixture updateables and autonomous sounds are quiesced.");
    }

    private ActionReceipt TriggerCreatureSound(
        AutomationCommandContext context,
        TriggerCreatureSoundCommand command) {
        if (!Enum.IsDefined(command.SoundKind) || command.EntityId <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Creature sound kind must be known and entityId must be positive.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentCreatureSounds sounds = ResolveFixtureComponent<ComponentCreatureSounds>(
            project,
            command.EntityId);
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "creatureSoundLocal",
            ActionEventPhase.Queued,
            target: command.EntityId.ToString(CultureInfo.InvariantCulture),
            code: command.SoundKind.ToString(),
            provenance: ActionProvenance.ProductionInput,
            productionEntry: ProductionPathEntries.CreatureSoundLocal);
        ConfigureFixtureSounds(sounds);
        switch (command.SoundKind) {
            case CreatureSoundKind.Idle:
                sounds.PlayIdleSound(skipIfRecentlyPlayed: false);
                break;
            case CreatureSoundKind.Pain:
                sounds.PlayPainSound();
                break;
            case CreatureSoundKind.Moan:
                sounds.PlayMoanSound();
                break;
            case CreatureSoundKind.Sneeze:
                sounds.PlaySneezeSound();
                break;
            case CreatureSoundKind.Cough:
                sounds.PlayCoughSound();
                break;
            case CreatureSoundKind.Puke:
                sounds.PlayPukeSound();
                break;
            case CreatureSoundKind.Attack:
                sounds.PlayAttackSound();
                break;
            default:
                throw new AutomationCommandException("invalid_payload", "Unknown sound kind.");
        }
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "creatureSoundLocal",
            ActionEventPhase.Observed,
            target: command.EntityId.ToString(CultureInfo.InvariantCulture),
            code: command.SoundKind.ToString(),
            provenance: ActionProvenance.ProductionInput,
            productionEntry: ProductionPathEntries.CreatureSoundLocal);
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            NetworkManager.PlayerIndex,
            "played_locally",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.CreatureSoundLocal);
    }

    private PlayerSurvivalStateResult SetPlayerSurvivalState(
        AutomationCommandContext context,
        SetPlayerSurvivalStateCommand command) {
        EnsureSurvivalServerRole("Player survival-state actions");
        ValidatePlayerSurvivalCommand(command);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolveFixturePlayer(project, command.Fixture, requireMainPlayer: false);
        ComponentClothing clothing = actor.Entity.FindComponent<ComponentClothing>(true);
        if (clothing.Id != command.Fixture.ClothingInventoryId
            || !ClothingSlot.ClothingSlotsByInt.TryGetValue(
                command.Fixture.ClothingSlotIndex,
                out ClothingSlot? clothingSlot)
            || clothingSlot == null) {
            throw new AutomationCommandException("fixture_mismatch", "Clothing fixture identity is stale.");
        }
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "playerSurvivalState",
            ActionEventPhase.Queued,
            command.Fixture.PlayerIndex,
            command.Fixture.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            "queued",
            provenance: ActionProvenance.AuthoritativeState,
            productionEntry: ProductionPathEntries.PlayerProgressionVitals);

        clothing.SetClothes(clothingSlot, [command.Fixture.ClothingValue]);
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Inventory,
            "clothingState",
            ActionEventPhase.Observed,
            command.Fixture.PlayerIndex,
            clothing.Id.ToString(CultureInfo.InvariantCulture),
            "production_hook_emitted",
            provenance: ActionProvenance.AuthoritativeState,
            productionEntry: ProductionPathEntries.ClothingState);
        actor.PlayerData.CharacterSkinName = command.Fixture.SkinName;
        QueueAuthoritativeState(new PlayerModifyPacket(actor.PlayerData));

        ComponentVitalStats vitals = actor.ComponentVitalStats;
        vitals.Food = command.Food;
        vitals.Stamina = command.Stamina;
        vitals.Sleep = command.Sleep;
        vitals.Wetness = command.Wetness;
        vitals.Temperature = command.Temperature;
        long vitalRevision = Advance(project, NetworkStateRevisionDomain.VitalStats, actor.Entity.Id);
        QueueAuthoritativeState(new VitalStatSyncPacket(vitals, vitalRevision));

        ComponentFlu flu = actor.ComponentFlu;
        flu.StartFlu();
        ModsManager.HookAction(
            "OnNetworkFluStarted",
            loader => loader.OnNetworkFluStarted(flu));
        if (command.TriggerFluSneeze) {
            flu.Sneeze();
            ModsManager.HookAction(
                "OnNetworkFluSneeze",
                loader => loader.OnNetworkFluSneeze(flu));
        }

        ComponentSickness sickness = actor.ComponentSickness;
        sickness.StartSickness();
        if (command.TriggerNausea) {
            sickness.NauseaEffect();
        }
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Mechanics,
            "statusEffects",
            ActionEventPhase.Observed,
            command.Fixture.PlayerIndex,
            actor.Entity.Id.ToString(CultureInfo.InvariantCulture),
            "flu_and_sickness_hooks_emitted",
            provenance: ActionProvenance.AuthoritativeState,
            productionEntry: ProductionPathEntries.StatusEffects);

        actor.ComponentLevel.AddExperience(command.ExperienceCount, playSound: false);
        PlayerStats stats = actor.PlayerStats;
        stats.BlocksDug = checked(stats.BlocksDug + command.BlocksDugIncrement);
        stats.HighestLevel = MathF.Max(stats.HighestLevel, actor.PlayerData.Level);
        long statsRevision = Advance(project, NetworkStateRevisionDomain.PlayerStats, actor.Entity.Id);
        QueueAuthoritativeState(new PlayerStatSyncPacket(actor, statsRevision));

        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "playerSurvivalState",
            ActionEventPhase.Applied,
            command.Fixture.PlayerIndex,
            command.Fixture.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            "authoritative_state_queued",
            data: new Dictionary<string, string> {
                ["level"] = actor.PlayerData.Level.ToString("R", CultureInfo.InvariantCulture),
                ["blocksDug"] = stats.BlocksDug.ToString(CultureInfo.InvariantCulture),
                ["vitalRevision"] = vitalRevision.ToString(CultureInfo.InvariantCulture),
                ["statsRevision"] = statsRevision.ToString(CultureInfo.InvariantCulture)
            },
            provenance: ActionProvenance.AuthoritativeState,
            productionEntry: ProductionPathEntries.PlayerProgressionVitals);
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.Fixture.PlayerIndex,
            "applied",
            Provenance: ActionProvenance.AuthoritativeState,
            ProductionEntry: ProductionPathEntries.PlayerProgressionVitals);
        return new PlayerSurvivalStateResult(
            receipt,
            actor.PlayerData.Level,
            stats.BlocksDug,
            command.Fixture.ClothingValue);
    }

    private ActionReceipt RequestSkinTransfer(
        AutomationCommandContext context,
        RequestSkinTransferCommand command) {
        EnsureClientActionRole("Skin transfer requests");
        if (!SkinPacketBase.TryValidateSkinName(command.SkinName, out string skinName)) {
            throw new AutomationCommandException("invalid_payload", "Skin name is unsafe.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        QueueProductionRequest(new RequestSkinPacket(skinName));
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "skinTransfer",
            ActionEventPhase.Queued,
            actor.PlayerData.PlayerIndex,
            skinName,
            "request_queued",
            provenance: ActionProvenance.ProductionRequest,
            productionEntry: ProductionPathEntries.SkinTransfer);
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "skinTransfer",
            ActionEventPhase.Observed,
            actor.PlayerData.PlayerIndex,
            skinName,
            "production_request_emitted",
            provenance: ActionProvenance.ProductionRequest,
            productionEntry: ProductionPathEntries.SkinTransfer);
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            actor.PlayerData.PlayerIndex,
            "queued",
            Provenance: ActionProvenance.ProductionRequest,
            ProductionEntry: ProductionPathEntries.SkinTransfer);
    }

    private SurvivalStateRevisionBurstResult RunSurvivalStateRevisionBurst(
        AutomationCommandContext context,
        RunSurvivalStateRevisionBurstCommand command) {
        EnsureSurvivalServerRole("Survival-state revision bursts");
        ValidateFixture(command.Fixture);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolveFixturePlayer(project, command.Fixture, requireMainPlayer: false);
        List<Packet> staleStates = [];
        List<long> finalRevisions = [];
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.State,
            "survivalStateRevisionBurst",
            ActionEventPhase.Queued,
            command.Fixture.PlayerIndex,
            command.Fixture.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            "queued",
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.SurvivalStateFault);

        ComponentChaseBehavior chase = ResolveFixtureComponent<ComponentChaseBehavior>(project, command.Fixture.Behaviors.ChaseEntityId);
        chase.IsAttack = false;
        staleStates.Add(new ChaseBehaviorPacket(chase, false, Current(project, NetworkStateRevisionDomain.CreatureChase, chase.Entity.Id)));
        chase.IsAttack = true;
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureChase, chase.Entity.Id));

        ComponentDigInMudBehavior dig = ResolveFixtureComponent<ComponentDigInMudBehavior>(project, command.Fixture.Behaviors.DigInMudEntityId);
        dig.IsDigIn = false;
        staleStates.Add(new DigInMudBehaviorPacket(dig, false, Current(project, NetworkStateRevisionDomain.CreatureDigInMud, dig.Entity.Id)));
        dig.IsDigIn = true;
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureDigInMud, dig.Entity.Id));

        ComponentEatPickableBehavior eat = ResolveFixtureComponent<ComponentEatPickableBehavior>(project, command.Fixture.Behaviors.EatPickableEntityId);
        eat.IsFeed = false;
        staleStates.Add(new EatPickableBehaviorPacket(eat, false, Current(project, NetworkStateRevisionDomain.CreatureEatPickable, eat.Entity.Id)));
        eat.IsFeed = true;
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureEatPickable, eat.Entity.Id));

        ComponentFishOutOfWaterBehavior fish = ResolveFixtureComponent<ComponentFishOutOfWaterBehavior>(project, command.Fixture.Behaviors.FishOutOfWaterEntityId);
        fish.IsBend = false;
        staleStates.Add(new FishOutOfWaterBehaviorPacket(fish, false, Current(project, NetworkStateRevisionDomain.CreatureFishOutOfWater, fish.Entity.Id)));
        fish.IsBend = true;
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureFishOutOfWater, fish.Entity.Id));

        ComponentLookAroundBehavior look = ResolveFixtureComponent<ComponentLookAroundBehavior>(project, command.Fixture.Behaviors.LookAroundEntityId);
        look.IsLookRandom = false;
        staleStates.Add(new LookAroundBehaviorPacket(look, false, Current(project, NetworkStateRevisionDomain.CreatureLookAround, look.Entity.Id)));
        look.IsLookRandom = true;
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureLookAround, look.Entity.Id));

        ComponentRandomFeedBehavior feed = ResolveFixtureComponent<ComponentRandomFeedBehavior>(project, command.Fixture.Behaviors.RandomFeedEntityId);
        feed.IsFeed = false;
        staleStates.Add(new RandomFeedBehaviorPacket(feed, false, Current(project, NetworkStateRevisionDomain.CreatureRandomFeed, feed.Entity.Id)));
        feed.IsFeed = true;
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureRandomFeed, feed.Entity.Id));

        ComponentRandomPeckBehavior peck = ResolveFixtureComponent<ComponentRandomPeckBehavior>(project, command.Fixture.Behaviors.RandomPeckEntityId);
        peck.IsFeed = false;
        staleStates.Add(new RandomPeckBehaviorPacket(peck, false, Current(project, NetworkStateRevisionDomain.CreatureRandomPeck, peck.Entity.Id)));
        peck.IsFeed = true;
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureRandomPeck, peck.Entity.Id));

        ComponentStubbornSteedBehavior stubborn = ResolveFixtureComponent<ComponentStubbornSteedBehavior>(project, command.Fixture.Behaviors.StubbornSteedEntityId);
        stubborn.SetHeadShakeOrder(0.15f);
        staleStates.Add(new StubbornSteedBehaviorPacket(stubborn, 0.15f, Current(project, NetworkStateRevisionDomain.CreatureStubbornSteed, stubborn.Entity.Id)));
        stubborn.SetHeadShakeOrder(0.85f);
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.CreatureStubbornSteed, stubborn.Entity.Id));

        ComponentClothing clothing = actor.Entity.FindComponent<ComponentClothing>(true);
        ClothingSlot slot = ClothingSlot.ClothingSlotsByInt[command.Fixture.ClothingSlotIndex];
        int[] finalClothes = [.. clothing.GetClothes(slot)];
        clothing.SetClothes(slot, []);
        staleStates.Add(new ClothingSyncPacket(clothing, Current(project, NetworkStateRevisionDomain.Clothing, actor.Entity.Id)));
        clothing.SetClothes(slot, finalClothes);
        finalRevisions.Add(Current(project, NetworkStateRevisionDomain.Clothing, actor.Entity.Id));

        ComponentVitalStats vitals = actor.ComponentVitalStats;
        float finalFood = vitals.Food;
        (float finalStamina, float finalSleep, float finalWetness, float finalTemperature) =
            (vitals.Stamina, vitals.Sleep, vitals.Wetness, vitals.Temperature);
        vitals.Food = 0.11f;
        long staleVitalRevision = Advance(project, NetworkStateRevisionDomain.VitalStats, actor.Entity.Id);
        staleStates.Add(new VitalStatSyncPacket(vitals, staleVitalRevision));
        vitals.Food = finalFood;
        vitals.Stamina = finalStamina;
        vitals.Sleep = finalSleep;
        vitals.Wetness = finalWetness;
        vitals.Temperature = finalTemperature;
        long finalVitalRevision = Advance(project, NetworkStateRevisionDomain.VitalStats, actor.Entity.Id);
        QueueAuthoritativeState(new VitalStatSyncPacket(vitals, finalVitalRevision));
        finalRevisions.Add(finalVitalRevision);

        double finalLevel = actor.PlayerData.Level;
        actor.PlayerData.Level = MathF.Max(1f, actor.PlayerData.Level - 0.25f);
        long staleProgressionRevision = Advance(project, NetworkStateRevisionDomain.PlayerProgression, actor.Entity.Id);
        staleStates.Add(new PlayerAddExperiencePacket(actor.PlayerData, 0, false, actor.PlayerData.Level, staleProgressionRevision));
        actor.PlayerData.Level = (float)finalLevel;
        long finalProgressionRevision = Advance(project, NetworkStateRevisionDomain.PlayerProgression, actor.Entity.Id);
        QueueAuthoritativeState(new PlayerAddExperiencePacket(actor.PlayerData, 0, false, actor.PlayerData.Level, finalProgressionRevision));
        finalRevisions.Add(finalProgressionRevision);

        PlayerStats stats = actor.PlayerStats;
        long finalBlocksDug = stats.BlocksDug;
        stats.BlocksDug = Math.Max(0, stats.BlocksDug - 1);
        long staleStatsRevision = Advance(project, NetworkStateRevisionDomain.PlayerStats, actor.Entity.Id);
        staleStates.Add(new PlayerStatSyncPacket(actor, staleStatsRevision));
        stats.BlocksDug = finalBlocksDug;
        long finalStatsRevision = Advance(project, NetworkStateRevisionDomain.PlayerStats, actor.Entity.Id);
        QueueAuthoritativeState(new PlayerStatSyncPacket(actor, finalStatsRevision));
        finalRevisions.Add(finalStatsRevision);

        ComponentFlu flu = actor.ComponentFlu;
        float finalFluDuration = flu.m_fluDuration;
        flu.m_fluDuration = 12f;
        long staleFluRevision = Advance(project, NetworkStateRevisionDomain.Flu, actor.Entity.Id);
        staleStates.Add(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat, staleFluRevision));
        flu.m_fluDuration = finalFluDuration;
        long finalFluRevision = Advance(project, NetworkStateRevisionDomain.Flu, actor.Entity.Id);
        QueueAuthoritativeState(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat, finalFluRevision));
        finalRevisions.Add(finalFluRevision);

        ComponentSickness sickness = actor.ComponentSickness;
        float finalSicknessDuration = sickness.m_sicknessDuration;
        sickness.m_sicknessDuration = 12f;
        long staleSicknessRevision = Advance(project, NetworkStateRevisionDomain.Sickness, actor.Entity.Id);
        staleStates.Add(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.SyncStat, staleSicknessRevision));
        sickness.m_sicknessDuration = finalSicknessDuration;
        long finalSicknessRevision = Advance(project, NetworkStateRevisionDomain.Sickness, actor.Entity.Id);
        QueueAuthoritativeState(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.SyncStat, finalSicknessRevision));
        finalRevisions.Add(finalSicknessRevision);

        if (command.SendStaleStates) {
            foreach (Packet staleState in staleStates) {
                QueueFaultInjectionAfterSettle(staleState);
            }
        }
        long minimumFinalRevision = finalRevisions.Min();
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.State,
            "survivalStateRevisionBurst",
            ActionEventPhase.Applied,
            command.Fixture.PlayerIndex,
            command.Fixture.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            command.SendStaleStates ? "stale_states_queued" : "final_states_queued",
            data: new Dictionary<string, string> {
                ["staleStateCount"] = staleStates.Count.ToString(CultureInfo.InvariantCulture),
                ["minimumFinalRevision"] = minimumFinalRevision.ToString(CultureInfo.InvariantCulture)
            },
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.SurvivalStateFault);
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.Fixture.PlayerIndex,
            command.SendStaleStates ? "stale_states_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.SurvivalStateFault);
        return new SurvivalStateRevisionBurstResult(
            receipt,
            finalLevel,
            finalBlocksDug,
            finalFood,
            finalFluDuration,
            finalSicknessDuration,
            minimumFinalRevision);
    }

    private ActionReceipt ForgeSurvivalState(
        AutomationCommandContext context,
        ForgeSurvivalStateCommand command) {
        EnsureClientActionRole("Survival-state authority probes");
        ValidateFixture(command.Fixture);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolveFixturePlayer(project, command.Fixture, requireMainPlayer: true);
        List<Packet> forged = [
            new ClothingSyncPacket(actor.Entity.FindComponent<ComponentClothing>(true)),
            new VitalStatSyncPacket(actor.ComponentVitalStats),
            new ComponentFluPacket(actor.ComponentFlu, ComponentFluPacket.EventType.SyncStat),
            new ComponentSicknessPacket(actor.ComponentSickness, ComponentSicknessPacket.EventType.SyncStat),
            new PlayerAddExperiencePacket(actor.PlayerData, 1, false, actor.PlayerData.Level),
            new PlayerStatSyncPacket(actor),
            new ChaseBehaviorPacket(ResolveFixtureComponent<ComponentChaseBehavior>(project, command.Fixture.Behaviors.ChaseEntityId), false),
            new DigInMudBehaviorPacket(ResolveFixtureComponent<ComponentDigInMudBehavior>(project, command.Fixture.Behaviors.DigInMudEntityId), false),
            new EatPickableBehaviorPacket(ResolveFixtureComponent<ComponentEatPickableBehavior>(project, command.Fixture.Behaviors.EatPickableEntityId), false),
            new FishOutOfWaterBehaviorPacket(ResolveFixtureComponent<ComponentFishOutOfWaterBehavior>(project, command.Fixture.Behaviors.FishOutOfWaterEntityId), false),
            new LookAroundBehaviorPacket(ResolveFixtureComponent<ComponentLookAroundBehavior>(project, command.Fixture.Behaviors.LookAroundEntityId), false),
            new RandomFeedBehaviorPacket(ResolveFixtureComponent<ComponentRandomFeedBehavior>(project, command.Fixture.Behaviors.RandomFeedEntityId), false),
            new RandomPeckBehaviorPacket(ResolveFixtureComponent<ComponentRandomPeckBehavior>(project, command.Fixture.Behaviors.RandomPeckEntityId), false),
            new StubbornSteedBehaviorPacket(ResolveFixtureComponent<ComponentStubbornSteedBehavior>(project, command.Fixture.Behaviors.StubbornSteedEntityId), 0f)
        ];
        foreach (Packet packet in forged) {
            QueueFaultInjection(packet);
        }
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.State,
            "survivalStateAuthorityProbe",
            ActionEventPhase.Queued,
            command.Fixture.PlayerIndex,
            command.Fixture.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            "forged_client_states_queued",
            data: new Dictionary<string, string> {
                ["packetCount"] = forged.Count.ToString(CultureInfo.InvariantCulture)
            },
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.SurvivalStateFault);
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.State,
            "survivalStateAuthorityProbe",
            ActionEventPhase.Observed,
            command.Fixture.PlayerIndex,
            command.Fixture.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            "forged_client_states_emitted",
            data: new Dictionary<string, string> {
                ["packetCount"] = forged.Count.ToString(CultureInfo.InvariantCulture)
            },
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.SurvivalStateFault);
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.Fixture.PlayerIndex,
            "queued",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.SurvivalStateFault);
    }

    private static Entity AddStableBehaviorEntity<TComponent>(
        Project project,
        string templateName,
        Vector3 position)
        where TComponent : class {
        Entity entity = CreateStableEntity(project, templateName, position);
        project.AddEntity(entity);
        TComponent component = entity.FindComponent<TComponent>()
            ?? throw new AutomationCommandException(
                "fixture_failed",
                $"Entity template '{templateName}' does not contain {typeof(TComponent).Name}.");
        if (component is IUpdateable updateable) {
            project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(updateable);
        }
        foreach (ComponentBehavior behavior in entity.FindComponents<ComponentBehavior>()) {
            if (behavior is IUpdateable behaviorUpdateable) {
                project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(behaviorUpdateable);
            }
        }
        if (entity.FindComponent<ComponentCreatureModel>() is IUpdateable modelUpdateable) {
            project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(modelUpdateable);
        }
        return entity;
    }

    private static (ClothingSlot Slot, int Value) FindFixtureClothing() {
        (ClothingSlot Slot, int Value, int RequiredLevel)? selected = null;
        foreach (Block block in BlocksManager.Blocks.OfType<ClothingBlock>()) {
            foreach (int value in block.GetCreativeValues()) {
                ClothingData? data = block.GetClothingData(value);
                if (data == null) continue;
                if (selected == null || data.PlayerLevelRequired < selected.Value.RequiredLevel) {
                    selected = (data.Slot, value, data.PlayerLevelRequired);
                }
            }
        }
        return selected is { } item
            ? (item.Slot, item.Value)
            : throw new AutomationCommandException(
                "fixture_failed",
                "No clothing value is registered in the loaded block catalog.");
    }

    private static byte[] CreateFixtureSkinData() {
        Image image = new(64, 64);
        for (int y = 0; y < image.Height; y++) {
            for (int x = 0; x < image.Width; x++) {
                image.SetPixel(x, y, new Color(
                    (byte)(40 + x),
                    (byte)(80 + y),
                    (byte)160,
                    (byte)255));
            }
        }
        using MemoryStream stream = new();
        Image.Save(image, stream, ImageFileFormat.Png, saveAlpha: true, sync: true);
        return stream.ToArray();
    }

    private static void ConfigureFixtureSounds(ComponentCreatureSounds sounds) {
        sounds.m_idleSound = "Audio/Creatures/Duck";
        sounds.m_painSound = "Audio/Creatures/Duck";
        sounds.m_moanSound = "Audio/Creatures/MaleMoan";
        sounds.m_sneezeSound = "Audio/Creatures/MaleSneeze";
        sounds.m_coughSound = "Audio/Creatures/MaleCough";
        sounds.m_pukeSound = "Audio/Creatures/MalePuke";
        sounds.m_attackSound = "Audio/Creatures/Duck";
        sounds.m_idleSoundMinDistance = 100f;
        sounds.m_painSoundMinDistance = 100f;
        sounds.m_moanSoundMinDistance = 100f;
        sounds.m_sneezeSoundMinDistance = 100f;
        sounds.m_coughSoundMinDistance = 100f;
        sounds.m_pukeSoundMinDistance = 100f;
        sounds.m_attackSoundMinDistance = 100f;
        sounds.m_lastSoundTime = -1000.0;
        sounds.m_lastCoughingSoundTime = -1000.0;
        sounds.m_lastPukeSoundTime = -1000.0;
    }

    private static void DisableFixtureSounds(ComponentCreatureSounds sounds) {
        sounds.m_idleSound = string.Empty;
        sounds.m_rareIdleSound = string.Empty;
        sounds.m_painSound = string.Empty;
        sounds.m_moanSound = string.Empty;
        sounds.m_sneezeSound = string.Empty;
        sounds.m_coughSound = string.Empty;
        sounds.m_pukeSound = string.Empty;
        sounds.m_attackSound = string.Empty;
        sounds.m_lastSoundTime = -1000.0;
        sounds.m_lastCoughingSoundTime = -1000.0;
        sounds.m_lastPukeSoundTime = -1000.0;
    }

    private static TComponent ResolveFixtureComponent<TComponent>(
        Project project,
        int entityId)
        where TComponent : class =>
        project.FindEntity(entityId)?.FindComponent<TComponent>()
        ?? throw new AutomationCommandException(
            "fixture_mismatch",
            $"Entity {entityId} does not expose {typeof(TComponent).Name}.");

    private static ComponentPlayer ResolveFixturePlayer(
        Project project,
        SurvivalStateFixture fixture,
        bool requireMainPlayer) {
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            fixture.PlayerIndex,
            requireMainPlayer);
        if (actor.Entity.Id != fixture.PlayerEntityId) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "The fixture player entity identity changed.");
        }
        return actor;
    }

    private void ValidateFixture(SurvivalStateFixture fixture) {
        if (fixture == null
            || survivalStateFixture != null && !Equals(survivalStateFixture, fixture)) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "The supplied survival-state fixture is not active on this node.");
        }
        ValidateBehaviorFixture(fixture.Behaviors);
    }

    private void ValidateBehaviorFixture(CreatureBehaviorFixture fixture) {
        if (fixture == null
            || fixture.EntityIds.Any(entityId => entityId <= 0)
            || fixture.EntityIds.Distinct().Count() != fixture.EntityIds.Length
            || survivalStateFixture != null
                && !Equals(survivalStateFixture.Behaviors, fixture)) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "The supplied creature behavior fixture is invalid or stale.");
        }
    }

    private static void ValidatePlayerSurvivalCommand(SetPlayerSurvivalStateCommand command) {
        if (command.Fixture == null
            || command.ExperienceCount is < 1 or > 10_000
            || command.BlocksDugIncrement is < 1 or > 1_000_000
            || !IsUnit(command.Food)
            || !IsUnit(command.Stamina)
            || !IsUnit(command.Sleep)
            || !IsUnit(command.Wetness)
            || !float.IsFinite(command.Temperature)
            || command.Temperature is < 0f or > 24f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Survival values, experience count, or stats increment are outside safe bounds.");
        }
    }

    private static bool IsUnit(float value) =>
        float.IsFinite(value) && value is >= 0f and <= 1f;

    private static long[] GetBehaviorRevisions(
        Project project,
        CreatureBehaviorFixture fixture) => [
        Current(project, NetworkStateRevisionDomain.CreatureChase, fixture.ChaseEntityId),
        Current(project, NetworkStateRevisionDomain.CreatureDigInMud, fixture.DigInMudEntityId),
        Current(project, NetworkStateRevisionDomain.CreatureEatPickable, fixture.EatPickableEntityId),
        Current(project, NetworkStateRevisionDomain.CreatureFishOutOfWater, fixture.FishOutOfWaterEntityId),
        Current(project, NetworkStateRevisionDomain.CreatureLookAround, fixture.LookAroundEntityId),
        Current(project, NetworkStateRevisionDomain.CreatureRandomFeed, fixture.RandomFeedEntityId),
        Current(project, NetworkStateRevisionDomain.CreatureRandomPeck, fixture.RandomPeckEntityId),
        Current(project, NetworkStateRevisionDomain.CreatureStubbornSteed, fixture.StubbornSteedEntityId)
    ];

    private static long Advance(
        Project project,
        NetworkStateRevisionDomain domain,
        int entityId) =>
        NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(project, domain, entityId);

    private static long Current(
        Project project,
        NetworkStateRevisionDomain domain,
        int entityId) =>
        NetworkStateRevisionTracker.GetAuthoritativeRevision(project, domain, entityId);

    private void EnsureSurvivalServerRole(string operation) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                $"{operation} must originate from the authoritative server node.");
        }
    }

    private void ResetSurvivalStateFixtures() => survivalStateFixture = null;
}
