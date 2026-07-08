using System;
using System.Collections.Generic;
using System.Text;
using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class RootManager {
    public static void Initialize() {
        ComponentChaseBehavior.OnIsAttackChanged += (behavior, isAttack) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new ChaseBehaviorPacket(behavior, isAttack));
        };
        ComponentCreatureSounds.OnPlaySound += (sounds, type, skipIfRecentlyPlayed) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new CreatureSoundPacket(sounds, type, skipIfRecentlyPlayed));
        };
        ComponentDamage.OnDamage += (damage) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new DamageSyncPacket(damage));
        };
        ComponentDigInMudBehavior.OnIsDigInChanged += (behavior, isDigIn) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new DigInMudBehaviorPacket(behavior, isDigIn));
        };
        ComponentEatPickableBehavior.OnIsFeedChanged += (behavior, isFeed) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new EatPickableBehaviorPacket(behavior, isFeed));
        };
        ComponentFishOutOfWaterBehavior.OnIsBendChanged += (behavior, isBend) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new FishOutOfWaterBehaviorPacket(behavior, isBend));
        };
        ComponentLookAroundBehavior.OnIsLookRandomChanged += (behavior, isLookRandom) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new LookAroundBehaviorPacket(behavior, isLookRandom));
        };
        ComponentFlu.OnFluEffectEvent += (flu) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.FluEffect));
        };
        ComponentFlu.OnFluUpdate += (flu) => {
            if (!NetworkManager.IsNetworkMode && Time.PeriodicEvent(1.0, 0.5))
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat));
        };
        ComponentFlu.OnSneezeEvent += (flu) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.Sneeze));
        };
        ComponentFlu.OnStartFluEvent += (flu) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.StartFlu));
        };
        ComponentFurnace.OnFurnaceUpdate += (furnace) => {
            if (NetworkManager.IsServerMode)
                NetworkManager.Queue(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel));
        };
        ComponentGui.OnManualLightingStrike += (gui, position, direction) =>
            NetworkManager.Queue(new SubsystemSkyPacket(position, direction));
        ComponentGui.OnManualTimeChange += (gui, totalElapsedGameTime, timeOfDayOffset) =>
            NetworkManager.Queue(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        ComponentGui.OnManualWeatherChange += (gui, type) =>
            NetworkManager.Queue(new SubsystemWeatherPacket(type));
        ComponentGui.OnPlayerCreativeFly += (gui, player) =>
            NetworkManager.Queue(new PlayerCreativeFlyPacket(player));
        ComponentHealth.OnCreatureDied += (health) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new HealthSyncPacket(health));
        };
        ComponentHealth.OnHealthFallDamage += InjureRequestPacket.SendFallDamage;
        ComponentHealth.OnHealthHealUpdate += (health) => {
            if (!NetworkManager.IsNetworkMode && Time.PeriodicEvent(2.0, 0.0))
                NetworkManager.Queue(new HealthSyncPacket(health));
        };
        ComponentHealth.OnHealthInjured += (health, injury, amount) => {
            if (!NetworkManager.IsNetworkMode && amount > 0f)
                NetworkManager.Queue(new InjureEventPacket(health, injury.Attacker, amount, injury.Cause, injury.IgnoreInvulnerability));
        };
        ComponentHumanModel.OnRow += (model) =>
            NetworkManager.Queue(new HumanRowBehaviorPacket(model));
        ComponentInventoryBase.OnSlotChanged += (inventory, slotIndex) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new InventorySyncPacket(inventory));
        };
        ComponentLevel.OnAddExperience += (level, count, playSound) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new PlayerAddExperiencePacket(level.m_componentPlayer.PlayerData, count, playSound, level.m_componentPlayer.PlayerData.Level));
        };
        ComponentOnFire.OnSetOnFire += (onFire, attacker, duration) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new EntityOnFirePacket(onFire, attacker, duration));
        };
        SubsystemParticles.OnParticleSystemAdded += particleSystem => {
            if (NetworkManager.IsNetworkMode || particleSystem is not HitValueParticleSystem hitValueParticleSystem) {
                return;
            }
            HitValueParticleSystem.Particle particle = hitValueParticleSystem.Particles[0];
            NetworkManager.Queue(new HitValuePacket(particle.Position, particle.Velocity, particle.BaseColor, particle.Text, true));
        };
        ComponentPickableGatherer.OnPickableAttract += (pickable) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new PickableSetFlyToPositionPacket(pickable));
        };
        ComponentPlayer.OnActiveSlotChanged += (player) => {
            if (player.ComponentMiner?.Inventory != null)
                NetworkManager.Queue(new ActiveSlotChangePacket(player.ComponentMiner.Inventory));
        };
        ComponentPlayer.OnAimEvent += (player) =>
            NetworkManager.Queue(new PlayerAimPacket(player));
        ComponentPlayer.OnDigEvent += (player) =>
            NetworkManager.Queue(new PlayerDigPacket(player));
        ComponentPlayer.OnHit += (player, hitBody, hitPoint, direction) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new PlayerHitPacket(player, hitBody, hitPoint, direction));
        };
        ComponentPlayer.OnInteractEvent += (player) =>
            NetworkManager.Queue(new PlayerInteractPacket(player));
        ComponentPlayer.OnDropEvent += (player) =>
            NetworkManager.Queue(new PlayerDropPacket(player));
        ComponentRandomFeedBehavior.OnIsFeedChanged += (behavior, isFeed) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new RandomFeedBehaviorPacket(behavior, isFeed));
        };
        ComponentRandomPeckBehavior.OnIsFeedChanged += (behavior, isPeck) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new RandomPeckBehaviorPacket(behavior, isPeck));
        };
        ComponentRider.OnDismounting += (rider, isRequest) =>
            NetworkManager.Queue(new ComponentMountPacket(rider, isRequest));
        ComponentRider.OnMounting += (rider, mount, isRequest) =>
            NetworkManager.Queue(new ComponentMountPacket(rider, mount, isRequest));
        ComponentSickness.OnSicknessChange += (sickness) => {
            if (NetworkManager.IsServerMode)
                NetworkManager.Queue(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.SyncStat));
        };
        ComponentSickness.OnNauseaEffect += (sickness) => {
            if (NetworkManager.IsServerMode)
                NetworkManager.Queue(new ComponentSicknessPacket(sickness, ComponentSicknessPacket.EventType.NauseaEffect));
        };
        ComponentStubbornSteedBehavior.OnHeadShakeOrder += (behavior, headShakeOrder) => {
            if (NetworkManager.IsServerMode)
                NetworkManager.Queue(new StubbornSteedBehaviorPacket(behavior, headShakeOrder));
        };
        ComponentVitalStats.OnVitalStatsUpdate += (stats) => {
            if (NetworkManager.IsServerMode) {
                NetworkManager.Queue(new VitalStatSyncPacket(stats));
            }
        };
        SubsystemAdjustableDelayGateBlockBehavior.OnEditBlockDelay += (subsystem, cell, newDelay) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new AdjustableDelayGatePacket(cell, false, default, default, newDelay));
        };
        SubsystemAdjustableDelayGateBlockBehavior.OnEditItemDelay += (subsystem, inventoryId, slotIndex, newDelay) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new AdjustableDelayGatePacket(default, true, inventoryId, slotIndex, newDelay));
        };
        SubsystemBatteryBlockBehavior.OnEditBlockVoltage += (subsystem, cell, newVoltageLevel) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new BatteryVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemBatteryBlockBehavior.OnEditItemVoltage += (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new BatteryVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemButtonBlockBehavior.OnEditBlockVoltage += (subsystem, cell, newVoltageLevel) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new ButtonVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemButtonBlockBehavior.OnEditItemVoltage += (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new ButtonVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemElectricity.OnElectricityPersistentVoltageChanged += (subsystem, list) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new SubsystemElectricityPacket(list));
        };
        SubsystemEntityBlockBehavior.OnOpenInventoryRequest += (subsystem, point) => {
            if (NetworkManager.IsNetworkMode)
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
            if (NetworkManager.IsServerMode)
                NetworkManager.Queue(new ComponentFurnacePacket(furnaces));
        };
        SubsystemGameInfo.OnTimeUpdate += (totalElapsedGameTime, timeOfDayOffset) =>
            NetworkManager.Queue(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        SubsystemMemoryBankBlockBehavior.OnEditBlockData += (subsystem, cell, data) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new MemoryBankDataPacket(cell, false, default, default, data));
        };
        SubsystemMemoryBankBlockBehavior.OnEditItemData += (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new MemoryBankDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemMemoryBankBlockBehavior.OnItemDataHarvested += (inventoryId, data) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new MemoryBankDataPacket(default, true, inventoryId, -1, data));
        };
        SubsystemMovingBlocks.OnMovingBlockAdd += (movingBlockSet) =>
            NetworkManager.Queue(new MovingBlockAddPacket((SubsystemMovingBlocks.MovingBlockSet)movingBlockSet));
        SubsystemMovingBlocks.OnMovingBlockRemove += (movingBlockSet, isCrash) =>
            NetworkManager.Queue(new MovingBlockRemovePacket(movingBlockSet, isCrash));
        SubsystemPickables.OnPickableCreate += (pickable) =>
            NetworkManager.Queue(new PickableCreatePacket(pickable));
        SubsystemPickables.OnPickableDelete += (pickable) =>
            NetworkManager.Queue(new PickableDeletePacket(pickable));
        SubsystemPickables.OnPickableRequestSync += (id) =>
            NetworkManager.Queue(new PickableRequestSyncPacket(id));
        SubsystemPistonBlockBehavior.OnEditBlockData += (subsystem, cell, data) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new PistonDataPacket(cell, false, default, default, data));
        };
        SubsystemPistonBlockBehavior.OnEditItemData += (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new PistonDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemProjectiles.OnProjectileFire += (projectile) =>
            NetworkManager.Queue(new ProjectilePacket(projectile));
        SubsystemSignBlockBehavior.OnEditSignRequest += (subsystem, point) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new EditSignPacket(point));
        };
        SubsystemSignBlockBehavior.OnSignDataChanged += (subsystem, point, lines, colors, url) =>
            NetworkManager.Queue(new SignBlockPacket(point, lines, colors, url));
        SubsystemSky.OnSkyLightning += (targetPosition) =>
            NetworkManager.Queue(new SubsystemSkyPacket(targetPosition));
        SubsystemSwitchBlockBehavior.OnEditBlockVoltage += (subsystem, cell, newVoltageLevel) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new SwitchVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemSwitchBlockBehavior.OnEditItemVoltage += (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new SwitchVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemTerrain.OnTerrainChangeCell += (subsystem, x, y, z, value) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new TerrainChangeCellPacket(x, y, z, value));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnEditBlockData += (subsystem, cell, data) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new TruthTableDataPacket(cell, false, default, default, data));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnEditItemData += (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new TruthTableDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnItemDataHarvested += (inventoryId, data) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new TruthTableDataPacket(default, true, inventoryId, -1, data));
        };
        SubsystemWeather.OnFogChange += (startTime, rampTime, endTime) =>
            NetworkManager.Queue(new SubsystemWeatherPacket(startTime, rampTime, endTime));
        SubsystemWeather.OnPrecipitationChange += (startTime, endTime, intensity) =>
            NetworkManager.Queue(new SubsystemWeatherPacket(startTime, endTime, intensity));
        TerrainUpdater.OnTerrainPlayerLocationUpdate += (updater, location) =>
            NetworkManager.Queue(new PlayerUpdateLocationPacket(location));
        TerrainUpdater.OnTerrainReplyResult += (updater, playerIndex, chunks) =>
            NetworkManager.Queue(new TerrainReplyResultPacket(chunks) {
                To = Enumerable.FirstOrDefault(
                        NetworkManager.NetworkServer?.ClientSessions.Values, c => c.PlayerIndex == playerIndex)
            });
        TerrainUpdater.OnTerrainRequestSyncChunks += (updater, chunks) =>
            NetworkManager.Queue(new TerrainRequestSyncChunksPacket(chunks));
        TerrainUpdater.OnTerrainSyncChunkList += (updater, playerIndex, chunks) =>
            NetworkManager.Queue(new TerrainSyncChunkListPacket(chunks) {
                To = Enumerable.FirstOrDefault(
                        NetworkManager.NetworkServer?.ClientSessions.Values, c => c.PlayerIndex == playerIndex)
            });
        PlayerData.OnPlayerRestart += (playerData) => {
            if (NetworkManager.IsNetworkMode && playerData.IsMainPlayer)
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
            NetworkManager.NetworkClient?.MySession.NetworkState = state;
            NetworkManager.Queue(new StatePacket(NetworkManager.NetworkClient.MySession.PlayerIndex, state));
        };
        CrossbowWidget.OnCrossbowPull += (inventory, slotIndex) =>
            NetworkManager.Queue(new CrossbowPullPacket(inventory, slotIndex));
        DispenserWidget.OnDispenserChicked += (position, data) =>
            NetworkManager.Queue(new DispenserPacket(position, data));
        InventorySlotWidget.OnDragDropItem += (sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly) =>
            NetworkManager.Queue(new DragDropItemPacket(sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly));
        InventorySlotWidget.OnMoveItem += (sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count) =>
            NetworkManager.Queue(new MoveItemPacket(sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count));
        ViewWidget.OnPlayerDragDrop += (playerData, inventoryId, slotIndex, worldPos, count) =>
            NetworkManager.Queue(new PlayerDragDropPacket(playerData, inventoryId, slotIndex, worldPos, count));
        VitalStatsWidget.OnChokeRequest += (health, attacker, amount, cause, ignoreInvulnerability) =>
            NetworkManager.Queue(new InjureRequestPacket(health, attacker, amount, cause, ignoreInvulnerability, InjureRequestPacket.RequestInjureType.Choke));
        ComponentClothing.OnRequestSkin += (clothing, skinName) => {
            if (Time.PeriodicEvent(1.0, 0.0) && NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new RequestSkinPacket(skinName));
            if (Time.PeriodicEvent(1.0, 0.0) && !NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new WhoHasSkinPacket(skinName));
        };
        PlayerData.OnPlayerPlaying += (playerData) => {
            NetworkManager.NetworkClient?.MySession.NetworkState = NetworkState.Playing;
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new StatePacket(NetworkManager.NetworkClient.MySession.PlayerIndex, NetworkState.Playing));
        };
        ComponentSleep.OnSleepEvent += (sleep, allowManualWakeup) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new SleepRequestPacket(sleep, allowManualWakeup));
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new SleepStatusPacket(sleep, allowManualWakeup, true));
        };
        ComponentSleep.OnWakeUpEvent += (sleep) => {
            if (NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new WakeupRequestPacket(sleep));
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new WakeupStatusPacket(sleep));
        };
        ComponentPlayer.OnBodyUpdate += (player, isMainPlayer) => {
            if (!NetworkManager.IsNetworkMode && Time.PeriodicEvent(0.1f, 0.0))
                NetworkManager.Queue(new PlayerBodyUpdatePacket(player) {
                    Except = Enumerable.FirstOrDefault(
                        NetworkManager.NetworkServer?.ClientSessions.Values, c => c.PlayerIndex == player.PlayerData.PlayerIndex)
                });
            if (isMainPlayer && Time.PeriodicEvent(0.1f, 0.0))
                NetworkManager.Queue(new PlayerBodyUpdatePacket(player));
        };
        PlayerData.PlayerFirstUpdated += (player) => {
            // todo:非本地主玩家且缺少对应网络客户端时，转为离线状态
        };
        ComponentBody.IsNetworkMode += BodyUpdatePacket.ShouldBeNetworkControlled;
        ComponentLocomotion.IsNetworkMode += BodyUpdatePacket.ShouldBeNetworkControlled;
        ComponentBody.OnNetworkBodyUpdate += NetworkBodySmoothing.Update;
        ComponentBody.OnBodyRemoved += NetworkBodySmoothing.RemoveTarget;
        ComponentVitalStats.OnVitalStatsUpdate += (stats) => {
            if (NetworkManager.IsServerMode)
                NetworkManager.Queue(new VitalStatSyncPacket(stats));
        };
        ComponentBody.OnBodyUpdate += BodyUpdatePacket.CollectBodyForSync;
        TerrainUpdater.OnTerrainPlayerLocationUpdate += (updater, location) =>
            NetworkManager.Queue(new PlayerUpdateLocationPacket(location));
        ComponentFlu.OnFluUpdate += (flu) => {
            if (NetworkManager.IsNetworkMode && Time.PeriodicEvent(1.0, 0.5))
                NetworkManager.Queue(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat));
        };
        SubsystemFurnaceBlockBehavior.OnFurnacesUpdate += (subsystem, furnaces) => {
            if (NetworkManager.IsServerMode)
                NetworkManager.Queue(new ComponentFurnacePacket(furnaces));
        };
        SubsystemGameInfo.OnTimeUpdate += (totalElapsedGameTime, timeOfDayOffset) =>
            NetworkManager.Queue(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        ComponentFurnace.OnFurnaceUpdate += (furnace) => {
            if (NetworkManager.IsServerMode && Time.PeriodicEvent(2.0, 0.0))
                NetworkManager.Queue(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel));
        };
        Project.EntityAdded += (_, arg) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new EntityLoadPacket(arg.Entity));
        };
        Project.EntityRemoved += (_, arg) => {
            if (!NetworkManager.IsNetworkMode)
                NetworkManager.Queue(new EntityRemovePacket(arg.Entity.Id));
        };
    }
}
