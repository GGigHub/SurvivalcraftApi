using System;
using System.Collections.Generic;
using System.Text;
using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class RootManager {
    public static void Initialize() {
        ComponentChaseBehavior.OnIsAttackChanged += (behavior, isAttack) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new ChaseBehaviorPacket(behavior, isAttack));
        };
        ComponentCreatureSounds.OnPlaySound += (sounds, type, skipIfRecentlyPlayed) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new CreatureSoundPacket(sounds, type, skipIfRecentlyPlayed));
        };
        ComponentDamage.OnDamage += (damage) => {
            if (!NetworkManager.IsClientRunning && damage.Entity.Id > 0) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    damage.Entity.Project,
                    NetworkStateRevisionDomain.Damage,
                    damage.Entity.Id);
                NetworkManager.Queue(new DamageSyncPacket(damage, revision));
            }
        };
        ComponentDigInMudBehavior.OnIsDigInChanged += (behavior, isDigIn) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new DigInMudBehaviorPacket(behavior, isDigIn));
        };
        ComponentEatPickableBehavior.OnIsFeedChanged += (behavior, isFeed) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new EatPickableBehaviorPacket(behavior, isFeed));
        };
        ComponentFishOutOfWaterBehavior.OnIsBendChanged += (behavior, isBend) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new FishOutOfWaterBehaviorPacket(behavior, isBend));
        };
        ComponentLookAroundBehavior.OnIsLookRandomChanged += (behavior, isLookRandom) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new LookAroundBehaviorPacket(behavior, isLookRandom));
        };
        ComponentFlu.OnFluEffectEvent += (flu) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.FluEffect));
        };
        ComponentFlu.OnFluUpdate += (flu) => {
            if (!NetworkManager.IsClientRunning && Time.PeriodicEvent(1.0, 0.5))
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat));
        };
        ComponentFlu.OnSneezeEvent += (flu) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.Sneeze));
        };
        ComponentFlu.OnStartFluEvent += (flu) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.StartFlu));
        };
        ComponentFlu.OnFluImpulseEvent += (flu, impulse) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new ComponentFluPacket(flu, impulse));
        };
        ComponentFurnace.OnFurnaceUpdate += (furnace) => {
            if (NetworkManager.IsServerRunning && Time.PeriodicEvent(2.0, 0.0))
                NetworkManager.Queue(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel));
        };
        ComponentGui.OnManualLightingStrike += (gui, position, direction) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new SubsystemSkyPacket(position, direction));
        };
        ComponentGui.OnManualTimeChange += (gui, totalElapsedGameTime, timeOfDayOffset) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(SubsystemTimePacket.CreateRequest(timeOfDayOffset));
            else if (GameManager.Project != null) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    GameManager.Project,
                    NetworkStateRevisionDomain.Time,
                    SubsystemTimePacket.EnvironmentStateId);
                NetworkManager.Queue(new SubsystemTimePacket(
                    totalElapsedGameTime,
                    timeOfDayOffset,
                    revision));
            }
        };
        ComponentGui.OnManualWeatherChange += (gui, type) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new SubsystemWeatherPacket(type));
            else
                QueueAuthoritativeWeatherState();
        };
        ComponentGui.OnPlayerCreativeFly += (gui, player) =>
            NetworkManager.Queue(new PlayerCreativeFlyPacket(player));
        ComponentHealth.OnCreatureDied += (health) => {
            if (!NetworkManager.IsClientRunning && health.Entity.Id > 0) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    health.Entity.Project,
                    NetworkStateRevisionDomain.Health,
                    health.Entity.Id);
                NetworkManager.Queue(new HealthSyncPacket(health, revision));
            }
        };
        ComponentHealth.OnHealthFallDamage += InjureRequestPacket.SendFallDamage;
        ComponentHealth.OnHealthHealUpdate += (health) => {
            if (!NetworkManager.IsClientRunning
                && health.Entity.Id > 0
                && Time.PeriodicEvent(2.0, 0.0)) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    health.Entity.Project,
                    NetworkStateRevisionDomain.Health,
                    health.Entity.Id);
                NetworkManager.Queue(new HealthSyncPacket(health, revision));
            }
        };
        ComponentHealth.OnHealthInjured += (health, injury, amount) => {
            if (!NetworkManager.IsClientRunning && health.Entity.Id > 0 && amount > 0f) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    health.Entity.Project,
                    NetworkStateRevisionDomain.Health,
                    health.Entity.Id);
                NetworkManager.Queue(new InjureEventPacket(
                    health,
                    injury.Attacker,
                    amount,
                    injury.Cause,
                    injury.IgnoreInvulnerability,
                    revision));
            }
        };
        ComponentHumanModel.OnRow += (model) =>
            NetworkManager.Queue(new HumanRowBehaviorPacket(model));
        ComponentInventoryBase.OnSlotChanged += (inventory, slotIndex) => {
            if (!NetworkManager.IsClientRunning
                && inventory?.Id > 0
                && inventory is not ComponentClothing) {
                NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    inventory.Project,
                    NetworkStateRevisionDomain.Inventory,
                    inventory.Id);
                NetworkManager.Queue(new InventorySyncPacket(inventory));
            }
        };
        ComponentClothing.OnSlotChanged += (inventory, slotIndex) => {
            if (!NetworkManager.IsClientRunning && inventory is ComponentClothing clothing && clothing.Id > 0)
                NetworkManager.Queue(new ClothingSyncPacket(clothing));
        };
        ComponentLevel.OnAddExperience += (level, count, playSound) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new PlayerAddExperiencePacket(level.m_componentPlayer.PlayerData, count, playSound, level.m_componentPlayer.PlayerData.Level));
        };
        ComponentOnFire.OnSetOnFire += (onFire, attacker, duration) => {
            if (!NetworkManager.IsClientRunning && onFire.Entity.Id > 0) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    onFire.Entity.Project,
                    NetworkStateRevisionDomain.Fire,
                    onFire.Entity.Id);
                NetworkManager.Queue(new EntityOnFirePacket(onFire, attacker, revision));
            }
        };
        ComponentOnFire.OnExtinguish += onFire => {
            if (!NetworkManager.IsClientRunning && onFire.Entity.Id > 0) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    onFire.Entity.Project,
                    NetworkStateRevisionDomain.Fire,
                    onFire.Entity.Id);
                NetworkManager.Queue(new EntityOnFirePacket(onFire, revision: revision));
            }
        };
        SubsystemParticles.OnParticleSystemAdded += particleSystem => {
            if (NetworkManager.IsClientRunning || particleSystem is not HitValueParticleSystem hitValueParticleSystem) {
                return;
            }
            HitValueParticleSystem.Particle particle = hitValueParticleSystem.Particles[0];
            NetworkManager.Queue(new HitValuePacket(
                particle.Position,
                particle.Velocity,
                particle.BaseColor,
                particle.Text,
                true,
                hitValueParticleSystem.AttackerPlayerIndex));
        };
        ComponentPickableGatherer.OnPickableAttract += (pickable) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new PickableSetFlyToPositionPacket(pickable));
        };
        ComponentPlayer.OnActiveSlotChanged += (player) => {
            IInventory inventory = player.ComponentMiner?.Inventory;
            if (inventory == null || inventory.Id <= 0) {
                return;
            }
            if (NetworkManager.IsClientRunning) {
                if (player.PlayerData.IsMainPlayer) {
                    NetworkManager.Queue(new ActiveSlotChangePacket(inventory));
                }
            }
            else {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    inventory.Project,
                    NetworkStateRevisionDomain.Inventory,
                    inventory.Id);
                NetworkManager.Queue(new ActiveSlotChangePacket(inventory, revision));
            }
        };
        ComponentPlayer.OnAimEvent += (player) =>
            NetworkManager.Queue(new PlayerAimPacket(player));
        ComponentPlayer.OnDigEvent += (player) =>
            NetworkManager.Queue(new PlayerDigPacket(player));
        ComponentPlayer.OnHit += (player, hitBody, hitPoint, direction) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new PlayerHitPacket(player, hitBody, hitPoint, direction));
        };
        ComponentPlayer.OnInteractEvent += (player) =>
            NetworkManager.Queue(new PlayerInteractPacket(player));
        ComponentPlayer.OnDropEvent += (player) =>
            NetworkManager.Queue(new PlayerDropPacket(player));
        ComponentRandomFeedBehavior.OnIsFeedChanged += (behavior, isFeed) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new RandomFeedBehaviorPacket(behavior, isFeed));
        };
        ComponentRandomPeckBehavior.OnIsFeedChanged += (behavior, isPeck) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new RandomPeckBehaviorPacket(behavior, isPeck));
        };
        ComponentRider.OnDismounting += (rider, isRequest) =>
            NetworkManager.Queue(new ComponentMountPacket(rider, isRequest));
        ComponentRider.OnMounting += (rider, mount, isRequest) =>
            NetworkManager.Queue(new ComponentMountPacket(rider, mount, isRequest));
        ComponentSickness.OnSicknessChange += (sickness) => {
            if (NetworkManager.IsServerRunning)
                NetworkManager.Queue(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.SyncStat));
        };
        ComponentSickness.OnNauseaEffect += (sickness) => {
            if (NetworkManager.IsServerRunning)
                NetworkManager.Queue(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.NauseaEffect));
        };
        ComponentStubbornSteedBehavior.OnHeadShakeOrder += (behavior, headShakeOrder) => {
            if (NetworkManager.IsServerRunning)
                NetworkManager.Queue(new StubbornSteedBehaviorPacket(behavior, headShakeOrder));
        };
        ComponentVitalStats.OnVitalStatsUpdate += (stats) => {
            if (NetworkManager.IsServerRunning) {
                NetworkManager.Queue(new VitalStatSyncPacket(stats));
            }
        };
        SubsystemAdjustableDelayGateBlockBehavior.OnEditBlockDelay += (subsystem, cell, newDelay) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new AdjustableDelayGatePacket(cell, false, default, default, newDelay));
        };
        SubsystemAdjustableDelayGateBlockBehavior.OnEditItemDelay += (subsystem, inventoryId, slotIndex, newDelay) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new AdjustableDelayGatePacket(default, true, inventoryId, slotIndex, newDelay));
        };
        SubsystemBatteryBlockBehavior.OnEditBlockVoltage += (subsystem, cell, newVoltageLevel) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new BatteryVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemBatteryBlockBehavior.OnEditItemVoltage += (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new BatteryVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemButtonBlockBehavior.OnEditBlockVoltage += (subsystem, cell, newVoltageLevel) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new ButtonVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemButtonBlockBehavior.OnEditItemVoltage += (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new ButtonVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemElectricity.OnElectricityPersistentVoltageChanged += (subsystem, list) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new SubsystemElectricityPacket(list));
        };
        SubsystemEntityBlockBehavior.OnOpenInventoryRequest += (subsystem, point) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new OpenInventoryByPointPacket(point));
        };
        SubsystemExplosions.OnExplosionCell += (explosionCells) =>
            NetworkManager.Queue(new ExplosionCellPacket(explosionCells));
        SubsystemExplosions.OnExplosionSound += (position, pressure, delay) =>
            NetworkManager.Queue(new ExplosionSoundPacket(position, pressure, delay));
        SubsystemFireBlockBehavior.OnFireBlockAdd += (x, y, z, expandability) =>
            NetworkManager.Queue(new FireBlockAddPacket(x, y, z, expandability));
        SubsystemFireBlockBehavior.OnFireBlockRemove += (x, y, z) =>
            NetworkManager.Queue(new FireBlockRemovePacket(x, y, z));
        SubsystemFurnaceBlockBehavior.OnFurnacesUpdate += (subsystem, furnaces) => {
            if (NetworkManager.IsServerRunning)
                NetworkManager.Queue(new ComponentFurnacePacket(furnaces));
        };
        SubsystemGameInfo.OnTimeUpdate += (totalElapsedGameTime, timeOfDayOffset) => {
            if (!NetworkManager.IsClientRunning && GameManager.Project != null) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    GameManager.Project,
                    NetworkStateRevisionDomain.Time,
                    SubsystemTimePacket.EnvironmentStateId);
                NetworkManager.Queue(new SubsystemTimePacket(
                    totalElapsedGameTime,
                    timeOfDayOffset,
                    revision));
            }
        };
        SubsystemMemoryBankBlockBehavior.OnEditBlockData += (subsystem, cell, data) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new MemoryBankDataPacket(cell, false, default, default, data));
        };
        SubsystemMemoryBankBlockBehavior.OnEditItemData += (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new MemoryBankDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemMemoryBankBlockBehavior.OnItemDataHarvested += (id, data) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new MemoryBankDataPacket(id, data));
        };
        SubsystemMovingBlocks.OnMovingBlockAdd += (movingBlockSet) => {
            if (NetworkManager.IsServerRunning
                && !NetworkManager.IsClientRunning
                && GameManager.Project != null) {
                NetworkManager.Queue(new MovingBlockAddPacket((SubsystemMovingBlocks.MovingBlockSet)movingBlockSet));
            }
        };
        SubsystemMovingBlocks.OnMovingBlockRemove += (movingBlockSet, isCrash) =>
            NetworkManager.Queue(new MovingBlockRemovePacket(movingBlockSet, isCrash));
        SubsystemPickables.OnPickableCreate += (pickable) =>
            NetworkManager.Queue(new PickableCreatePacket(pickable));
        SubsystemPickables.OnPickableDelete += (pickable) =>
            NetworkManager.Queue(new PickableDeletePacket(pickable));
        SubsystemPickables.OnPickableRequestSync += (id) =>
            NetworkManager.Queue(new PickableRequestSyncPacket(id));
        SubsystemPistonBlockBehavior.OnEditBlockData += (subsystem, cell, data) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new PistonDataPacket(cell, false, default, default, data));
        };
        SubsystemPistonBlockBehavior.OnEditItemData += (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new PistonDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemProjectiles.OnProjectileFire += (projectile, isFireProjectile) =>
            NetworkManager.Queue(new ProjectilePacket(projectile, isFireProjectile));
        SubsystemSignBlockBehavior.OnEditSignRequest += (subsystem, point) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new EditSignPacket(point));
        };
        SubsystemSignBlockBehavior.OnSignDataChanged += (subsystem, point, lines, colors, url) =>
            NetworkManager.Queue(new SignBlockPacket(point, lines, colors, url));
        SubsystemSky.OnSkyLightning += targetPosition => {
            if (!NetworkManager.IsClientRunning && GameManager.Project != null) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    GameManager.Project,
                    NetworkStateRevisionDomain.Sky,
                    SubsystemTimePacket.EnvironmentStateId);
                NetworkManager.Queue(new SubsystemSkyPacket(targetPosition, revision));
            }
        };
        SubsystemSwitchBlockBehavior.OnEditBlockVoltage += (subsystem, cell, newVoltageLevel) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new SwitchVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemSwitchBlockBehavior.OnEditItemVoltage += (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new SwitchVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemTerrain.OnTerrainChangeCell += (subsystem, x, y, z, value) => {
            if (!NetworkManager.IsClientRunning) {
                long revision = TerrainRevisionTracker.AdvanceAuthoritativeRevision(
                    subsystem,
                    x >> 4,
                    z >> 4);
                NetworkManager.Queue(new TerrainChangeCellPacket(x, y, z, value, revision));
            }
        };
        SubsystemTruthTableCircuitBlockBehavior.OnEditBlockData += (subsystem, cell, data) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new TruthTableDataPacket(cell, false, default, default, data));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnEditItemData += (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new TruthTableDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnItemDataHarvested += (id, data) => {
            if (!NetworkManager.IsClientRunning)
                NetworkManager.Queue(new TruthTableDataPacket(id, data));
        };
        SubsystemWeather.OnFogChange += (startTime, rampTime, endTime) =>
            QueueAuthoritativeWeatherState();
        SubsystemWeather.OnPrecipitationChange += (startTime, endTime, intensity, rampTime) =>
            QueueAuthoritativeWeatherState();
        TerrainUpdater.OnTerrainSyncChunkList += (updater, playerIndex, chunks) =>
            NetworkManager.Queue(new TerrainSyncChunkListPacket(chunks) {
                To = Enumerable.FirstOrDefault(
                        NetworkManager.ServerSessions, c => c.PlayerIndex == playerIndex)
            });
        PlayerData.OnPlayerRestart += (playerData) => {
            if (NetworkManager.IsClientRunning && playerData.IsMainPlayer)
                NetworkManager.Queue(new PlayerRestartPacket(playerData));
        };
        GameLoadingScreen.OnPlayerIntoPlaying += () => {
            ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>()?.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == NetworkManager.PlayerIndex);
            NetworkManager.Queue(new PlayerIntoPlayingPacket(mainPlayer));
        };
        PlayerScreen.OnPlayerCreate += (playerData) =>
            NetworkManager.Queue(new PlayerCreatePacket(playerData));
        PlayerScreen.OnPlayerModify += (playerData) =>
            NetworkManager.Queue(new PlayerModifyPacket(playerData));
        GameLoadingScreen.OnPlayerStateChanged += (state) => {
            NetworkManager.SetState(state);
            NetworkManager.Queue(new StatePacket(NetworkManager.PlayerIndex, state));
        };
        CrossbowWidget.OnCrossbowPull += (inventory, slotIndex) =>
            NetworkManager.Queue(new CrossbowPullPacket(inventory, slotIndex));
        DispenserWidget.OnDispenserChicked += (position, data) =>
            NetworkManager.Queue(new DispenserPacket(position, data));
        InventorySlotWidget.OnDragDropItem += (sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly, dragCount) =>
            NetworkManager.Queue(new DragDropItemPacket(sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly, dragCount));
        InventorySlotWidget.OnMoveItem += (sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count) =>
            NetworkManager.Queue(new MoveItemPacket(sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count));
        ViewWidget.OnPlayerDragDrop += (playerData, inventoryId, slotIndex, worldPos, count) =>
            NetworkManager.Queue(new PlayerDragDropPacket(playerData, inventoryId, slotIndex, worldPos, count));
        VitalStatsWidget.OnChokeRequest += (health, attacker, amount, cause, ignoreInvulnerability) =>
            NetworkManager.Queue(new InjureRequestPacket(health, attacker, amount, cause, ignoreInvulnerability, InjureRequestPacket.RequestInjureType.Choke));
        ComponentClothing.OnRequestSkin += (clothing, skinName) => {
            if (Time.PeriodicEvent(1.0, 0.0) && NetworkManager.IsClientRunning)
                NetworkManager.Queue(new RequestSkinPacket(skinName));
            if (Time.PeriodicEvent(1.0, 0.0) && !NetworkManager.IsClientRunning)
                NetworkManager.Queue(new WhoHasSkinPacket(skinName));
        };
        PlayerData.OnPlayerPlaying += (playerData) => {
            if (NetworkManager.IsClientRunning && !playerData.IsMainPlayer) {
                return;
            }
            NetworkManager.SetState(NetworkState.Playing);
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new StatePacket(NetworkManager.PlayerIndex, NetworkState.Playing));
        };
        ComponentSleep.OnSleepEvent += (sleep, allowManualWakeup) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new SleepRequestPacket(sleep, allowManualWakeup));
            if (!NetworkManager.IsClientRunning && sleep.Entity.Id > 0) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    sleep.Entity.Project,
                    NetworkStateRevisionDomain.Sleep,
                    sleep.Entity.Id);
                NetworkManager.Queue(new SleepStatusPacket(
                    sleep,
                    allowManualWakeup,
                    true,
                    revision: revision));
            }
        };
        ComponentSleep.OnWakeUpEvent += (sleep) => {
            if (NetworkManager.IsClientRunning)
                NetworkManager.Queue(new WakeupRequestPacket(sleep));
            if (!NetworkManager.IsClientRunning && sleep.Entity.Id > 0) {
                long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                    sleep.Entity.Project,
                    NetworkStateRevisionDomain.Sleep,
                    sleep.Entity.Id);
                NetworkManager.Queue(new WakeupStatusPacket(sleep, revision));
            }
        };
        ComponentPlayer.OnBodyUpdate += (player, isMainPlayer) => {
            if (!NetworkManager.IsClientRunning && Time.PeriodicEvent(0.2f, 0.0)) {
                foreach (ClientSession session in NetworkManager.ServerSessions) {
                    if (session.NetworkState < NetworkState.Playing
                        || session.PlayerIndex == player.PlayerData.PlayerIndex) {
                        continue;
                    }
                    NetworkManager.Queue(new PlayerBodyUpdatePacket(player, session) { To = session });
                }
            }
            if (isMainPlayer && Time.PeriodicEvent(0.2f, 0.0))
                NetworkManager.Queue(new PlayerBodyUpdatePacket(player));
        };
        PlayerData.PlayerFirstUpdated += (player) => {
            // todo:非本地主玩家且缺少对应网络客户端时，转为离线状态
        };
        TerrainUpdater.OnTerrainPlayerLocationUpdate += (updater, location) => {
            if (NetworkManager.IsClientRunning && NetworkManager.PlayerIndex >= 0) {
                NetworkManager.Queue(new TerrainViewDistancePacket(location));
            }
        };
        ComponentBody.IsNetworkMode += NetworkBodySyncHelpers.ShouldBeNetworkControlled;
        ComponentLocomotion.IsNetworkMode += NetworkBodySyncHelpers.ShouldBeNetworkControlled;
        ComponentBody.OnNetworkBodyUpdate += NetworkBodyInterpolation.Update;
        ComponentBody.OnBodyRemoved += NetworkBodyInterpolation.RemoveTarget;
        ComponentBody.OnBodyUpdate += BehaviorSyncPacket.CollectBehaviorForSync;
        Project.EntityAdded += (_, arg) => {
            if (!NetworkManager.IsClientRunning && arg.Entity.Id > 0) {
                EntityLifecycleTracker.AdvanceAuthoritativeRevision(
                    arg.Entity.Project,
                    arg.Entity.Id);
                NetworkManager.Queue(new EntityLoadPacket(arg.Entity));
            }
        };
        Project.EntityRemoved += (_, arg) => {
            if (!NetworkManager.IsClientRunning && arg.Entity.Id > 0) {
                long revision = EntityLifecycleTracker.AdvanceAuthoritativeRevision(
                    arg.Entity.Project,
                    arg.Entity.Id);
                NetworkManager.Queue(new EntityRemovePacket(arg.Entity.Id, revision));
            }
        };
    }

    private static void QueueAuthoritativeWeatherState() {
        if (NetworkManager.IsClientRunning || GameManager.Project == null) {
            return;
        }
        SubsystemWeather weather = GameManager.Project.FindSubsystem<SubsystemWeather>();
        if (weather == null) {
            return;
        }
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            GameManager.Project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId);
        NetworkManager.Queue(new SubsystemWeatherPacket(weather, revision));
    }
}
