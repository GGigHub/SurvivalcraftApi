using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network;

public static class RootManager {
    public static void Initialize() {
        ComponentChaseBehavior.OnIsAttackChanged = (behavior, isAttack) =>
            NetworkService.QueuePacket(new ChaseBehaviorPacket(behavior, isAttack), NetworkService.IsServer);
        ComponentCreatureSounds.OnPlaySound = (sounds, type, skipIfRecentlyPlayed) =>
            NetworkService.QueuePacket(new CreatureSoundPacket(sounds, type, skipIfRecentlyPlayed), NetworkService.IsServer);
        ComponentDamage.OnDamage = (damage) =>
            NetworkService.QueuePacket(new DamageSyncPacket(damage), NetworkService.IsServer);
        ComponentDigInMudBehavior.OnIsDigInChanged = (behavior, isDigIn) =>
            NetworkService.QueuePacket(new DigInMudBehaviorPacket(behavior, isDigIn), NetworkService.IsServer);
        ComponentEatPickableBehavior.OnIsFeedChanged = (behavior, isFeed) =>
            NetworkService.QueuePacket(new EatPickableBehaviorPacket(behavior, isFeed), NetworkService.IsServer);
        ComponentFishOutOfWaterBehavior.OnIsBendChanged = (behavior, isBend) =>
            NetworkService.QueuePacket(new FishOutOfWaterBehaviorPacket(behavior, isBend), NetworkService.IsServer);
        ComponentFlu.OnFluEffectEvent = (flu) =>
            NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.FluEffect), NetworkService.IsServer);
        ComponentFlu.OnFluUpdate = (flu) =>
            NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat), NetworkService.IsServer && Time.PeriodicEvent(1.0, 0.5));
        ComponentFlu.OnSneezeEvent = (flu) =>
            NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.Sneeze), NetworkService.IsServer);
        ComponentFlu.OnStartFluEvent = (flu) =>
            NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.StartFlu), NetworkService.IsServer);
        ComponentFurnace.OnFurnaceUpdate = (furnace) =>
            NetworkService.QueuePacket(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel), NetworkService.IsServer);
        ComponentGui.OnManualLightingStrike = (gui, position, direction) =>
            NetworkService.QueuePacket(new SubsystemSkyPacket(position, direction));
        ComponentGui.OnManualTimeUpdate = (gui, totalElapsedGameTime, timeOfDayOffset) =>
            NetworkService.QueuePacket(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        ComponentGui.OnManualWeatherUpdate = (gui, type) =>
            NetworkService.QueuePacket(new SubsystemWeatherPacket(type));
        ComponentGui.OnPlayerCreativeFly = (gui, player) =>
            NetworkService.QueuePacket(new PlayerCreativeFlyPacket(player));
        ComponentHealth.OnHealthDead = (health) =>
            NetworkService.QueuePacket(new HealthSyncPacket(health), NetworkService.IsServer);
        ComponentHealth.OnHealthFallDamage = InjureRequestPacket.SendFallDamage;
        ComponentHealth.OnHealthHealUpdate = (health) =>
            NetworkService.QueuePacket(new HealthSyncPacket(health), NetworkService.IsServer && Time.PeriodicEvent(2.0, 0.0));
        ComponentHealth.OnHealthInjured = (health, injury, amount) =>
            NetworkService.QueuePacket(new InjureEventPacket(health, injury.Attacker, amount, injury.Cause, injury.IgnoreInvulnerability), NetworkService.IsServer && amount > 0f);
        ComponentHumanModel.OnRow = (model) =>
            NetworkService.QueuePacket(new HumanRowBehaviorPacket(model));
        ComponentInventoryBase.OnSlotChanged = (inventory, slotIndex) =>
            NetworkService.QueuePacket(new InventorySyncPacket(inventory), NetworkService.IsServer);
        ComponentLevel.OnAddExperience = (level, count, playSound) =>
            NetworkService.QueuePacket(new PlayerAddExperiencePacket(level.m_componentPlayer.PlayerData, count, playSound, level.m_componentPlayer.PlayerData.Level), NetworkService.IsServer);
        ComponentOnFire.OnSetOnFire = (onFire, attacker, duration) =>
            NetworkService.QueuePacket(new EntityOnFirePacket(onFire, attacker, duration), NetworkService.IsServer);
        ComponentPickableGatherer.OnPickableAttract = (pickable) =>
            NetworkService.QueuePacket(new PickableSetFlyToPositionPacket(pickable), NetworkService.IsServer);
        ComponentPlayer.OnActiveSlotChanged = (player) =>
            NetworkService.QueuePacket(new ActiveSlotChangePacket(player.ComponentMiner.Inventory), player.ComponentMiner?.Inventory != null);
        ComponentPlayer.OnAimEvent = (player) =>
            NetworkService.QueuePacket(new PlayerAimPacket(player));
        ComponentPlayer.OnDigEvent = (player) =>
            NetworkService.QueuePacket(new PlayerDigPacket(player));
        ComponentPlayer.OnHit = (player, hitBody, hitPoint, direction) =>
            NetworkService.QueuePacket(new PlayerHitPacket(player, hitBody, hitPoint, direction), NetworkService.IsClient);
        ComponentPlayer.OnInteractEvent = (player) =>
            NetworkService.QueuePacket(new PlayerInteractPacket(player));
        ComponentRandomFeedBehavior.OnIsFeedChanged = (behavior, isFeed) =>
            NetworkService.QueuePacket(new RandomFeedBehaviorPacket(behavior, isFeed), NetworkService.IsServer);
        ComponentRandomPeckBehavior.OnIsFeedChanged = (behavior, isPeck) =>
            NetworkService.QueuePacket(new RandomPeckBehaviorPacket(behavior, isPeck), NetworkService.IsServer);
        ComponentRider.OnDismounting = (rider, isRequest) =>
            NetworkService.QueuePacket(new ComponentMountPacket(rider, isRequest));
        ComponentRider.OnMounting = (rider, mount, isRequest) =>
            NetworkService.QueuePacket(new ComponentMountPacket(rider, mount, isRequest));
        ComponentSickness.OnSicknessUpdate = (sickness) =>
            NetworkService.QueuePacket(new ComponentSicknessPacket(sickness), NetworkService.IsServer);
        ComponentVitalStats.OnVitalStatsUpdate = (stats) =>
            NetworkService.QueuePacket(new VitalStatSyncPacket(stats), NetworkService.IsServer && Time.PeriodicEvent(1.0, 0.5));
        SubsystemAdjustableDelayGateBlockBehavior.OnEditBlockDelay = (subsystem, cell, newDelay) =>
            NetworkService.QueuePacket(new AdjustableDelayGatePacket(cell, false, default, default, newDelay), NetworkService.IsClient);
        SubsystemAdjustableDelayGateBlockBehavior.OnEditItemDelay = (subsystem, inventoryId, slotIndex, newDelay) =>
            NetworkService.QueuePacket(new AdjustableDelayGatePacket(default, true, inventoryId, slotIndex, newDelay), NetworkService.IsClient);
        SubsystemBatteryBlockBehavior.OnEditBlockVoltage = (subsystem, cell, newVoltageLevel) =>
            NetworkService.QueuePacket(new BatteryVoltagePacket(cell, false, default, default, newVoltageLevel), NetworkService.IsClient);
        SubsystemBatteryBlockBehavior.OnEditItemVoltage = (subsystem, inventoryId, slotIndex, newVoltageLevel) =>
            NetworkService.QueuePacket(new BatteryVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel), NetworkService.IsClient);
        SubsystemButtonBlockBehavior.OnEditBlockVoltage = (subsystem, cell, newVoltageLevel) =>
            NetworkService.QueuePacket(new ButtonVoltagePacket(cell, false, default, default, newVoltageLevel), NetworkService.IsClient);
        SubsystemButtonBlockBehavior.OnEditItemVoltage = (subsystem, inventoryId, slotIndex, newVoltageLevel) =>
            NetworkService.QueuePacket(new ButtonVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel), NetworkService.IsClient);
        SubsystemElectricity.OnElectricityPersistentVoltageChanged = (subsystem, list) =>
            NetworkService.QueuePacket(new SubsystemElectricityPacket(list), !NetworkService.IsClient);
        SubsystemEntityBlockBehavior.OnOpenInventoryRequest = (subsystem, point) =>
            NetworkService.QueuePacket(new OpenInventoryByPointPacket(point), NetworkService.IsClient);
        SubsystemExplosions.OnExplosionCell = (explosionCells) =>
            NetworkService.QueuePacket(new ExplosionCellPacket(explosionCells));
        SubsystemExplosions.OnExplosionSound = (position, pressure, delay) =>
            NetworkService.QueuePacket(new ExplosionSoundPacket(position, pressure, delay));
        SubsystemFireBlockBehavior.OnFireBlockAdd = (x, y, z, expandability) =>
            NetworkService.QueuePacket(new FireBlockAddPacket(x, y, z, expandability));
        SubsystemFireBlockBehavior.OnFireBlockRemove = (x, y, z) =>
            NetworkService.QueuePacket(new FireBlockRemovePacket(x, y, z));
        SubsystemFurnaceBlockBehavior.OnFurnacesUpdate = (subsystem, furnaces) =>
            NetworkService.QueuePacket(new ComponentFurnacePacket(furnaces), NetworkService.IsServer);
        SubsystemGameInfo.OnTimeUpdate = (totalElapsedGameTime, timeOfDayOffset) =>
            NetworkService.QueuePacket(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        SubsystemMemoryBankBlockBehavior.OnEditBlockData = (subsystem, cell, data) =>
            NetworkService.QueuePacket(new MemoryBankDataPacket(cell, false, default, default, data), NetworkService.IsClient);
        SubsystemMemoryBankBlockBehavior.OnEditItemData = (subsystem, inventoryId, slotIndex, data) =>
            NetworkService.QueuePacket(new MemoryBankDataPacket(default, true, inventoryId, slotIndex, data), NetworkService.IsClient);
        SubsystemMemoryBankBlockBehavior.OnItemDataSync = (inventoryId, data) =>
            NetworkService.QueuePacket(new MemoryBankDataPacket(default, true, inventoryId, -1, data), !NetworkService.IsClient);
        SubsystemMovingBlocks.OnMovingBlockAdd = (movingBlockSet) =>
            NetworkService.QueuePacket(new MovingBlockAddPacket((SubsystemMovingBlocks.MovingBlockSet)movingBlockSet));
        SubsystemMovingBlocks.OnMovingBlockRemove = (movingBlockSet, isCrash) =>
            NetworkService.QueuePacket(new MovingBlockRemovePacket(movingBlockSet, isCrash));
        SubsystemPickables.OnPickableCreate = (pickable) =>
            NetworkService.QueuePacket(new PickableCreatePacket(pickable));
        SubsystemPickables.OnPickableDelete = (pickable) =>
            NetworkService.QueuePacket(new PickableDeletePacket(pickable));
        SubsystemPickables.OnPickableRequestSync = (id) =>
            NetworkService.QueuePacket(new PickableRequestSyncPacket(id));
        SubsystemPistonBlockBehavior.OnEditBlockData = (subsystem, cell, data) =>
            NetworkService.QueuePacket(new PistonDataPacket(cell, false, default, default, data), NetworkService.IsClient);
        SubsystemPistonBlockBehavior.OnEditItemData = (subsystem, inventoryId, slotIndex, data) =>
            NetworkService.QueuePacket(new PistonDataPacket(default, true, inventoryId, slotIndex, data), NetworkService.IsClient);
        SubsystemProjectiles.OnProjectileFire = (projectile) =>
            NetworkService.QueuePacket(new ProjectilePacket(projectile));
        SubsystemSignBlockBehavior.OnEditSignRequest = (subsystem, point) =>
            NetworkService.QueuePacket(new EditSignPacket(point), NetworkService.IsClient);
        SubsystemSignBlockBehavior.OnSignDataChanged = (subsystem, point, lines, colors, url) =>
            NetworkService.QueuePacket(new SignBlockPacket(point, lines, colors, url));
        SubsystemSky.OnSkyLightning = (targetPosition) =>
            NetworkService.QueuePacket(new SubsystemSkyPacket(targetPosition));
        SubsystemSwitchBlockBehavior.OnEditBlockVoltage = (subsystem, cell, newVoltageLevel) =>
            NetworkService.QueuePacket(new SwitchVoltagePacket(cell, false, default, default, newVoltageLevel), NetworkService.IsClient);
        SubsystemSwitchBlockBehavior.OnEditItemVoltage = (subsystem, inventoryId, slotIndex, newVoltageLevel) =>
            NetworkService.QueuePacket(new SwitchVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel), NetworkService.IsClient);
        SubsystemTerrain.OnTerrainChangeCell = (subsystem, x, y, z, value) =>
            NetworkService.QueuePacket(new TerrainChangeCellPacket(x, y, z, value), !NetworkService.IsClient);
        SubsystemTruthTableCircuitBlockBehavior.OnEditBlockData = (subsystem, cell, data) =>
            NetworkService.QueuePacket(new TruthTableDataPacket(cell, false, default, default, data), NetworkService.IsClient);
        SubsystemTruthTableCircuitBlockBehavior.OnEditItemData = (subsystem, inventoryId, slotIndex, data) =>
            NetworkService.QueuePacket(new TruthTableDataPacket(default, true, inventoryId, slotIndex, data), NetworkService.IsClient);
        SubsystemTruthTableCircuitBlockBehavior.OnItemDataSync = (inventoryId, data) =>
            NetworkService.QueuePacket(new TruthTableDataPacket(default, true, inventoryId, -1, data), !NetworkService.IsClient);
        SubsystemWeather.OnFogUpdate = (startTime, rampTime, endTime) =>
            NetworkService.QueuePacket(new SubsystemWeatherPacket(startTime, rampTime, endTime));
        SubsystemWeather.OnPrecipitationUpdate = (startTime, endTime, intensity) =>
            NetworkService.QueuePacket(new SubsystemWeatherPacket(startTime, endTime, intensity));
        TerrainUpdater.OnTerrainPlayerLocationUpdate = (updater, location) =>
            NetworkService.QueuePacket(new PlayerUpdateLocationPacket(location));
        TerrainUpdater.OnTerrainReplyResult = (updater, player, chunks) =>
            NetworkService.QueuePacket(new TerrainReplyResultPacket(chunks) { To = NetworkService.GetClientByGUID(player?.PlayerData?.NetworkGuid) });
        TerrainUpdater.OnTerrainRequestSyncChunks = (updater, chunks) =>
            NetworkService.QueuePacket(new TerrainRequestSyncChunksPacket(chunks));
        TerrainUpdater.OnTerrainSyncChunkList = (updater, player, chunks) =>
            NetworkService.QueuePacket(new TerrainSyncChunkListPacket(chunks) { To = NetworkService.GetClientByGUID(player?.PlayerData?.NetworkGuid) });
        PlayerData.OnPlayerRestart = (playerData) =>
            NetworkService.QueuePacket(new PlayerRestartPacket(playerData), NetworkService.IsClient && playerData.IsMainPlayer);
        GameLoadingScreen.OnPlayerIntoPlaying = () =>
            NetworkService.QueuePacket(new PlayerIntoPlayingPacket(NetworkService.MainPlayer));
        PlayerScreen.OnPlayerCreate = (playerData) =>
            NetworkService.QueuePacket(new PlayerCreatePacket(playerData));
        PlayerScreen.OnPlayerModify = (playerData) =>
            NetworkService.QueuePacket(new PlayerModifyPacket(playerData));
        GameLoadingScreen.OnPlayerStateChanged = (state) =>
            NetworkService.QueuePacket(new NetworkStateChangePacket((byte)(NetworkService.Self != null ? NetworkService.Self.ID : 0), state));
        CrossbowWidget.OnCrossbowPull = (inventory, slotIndex) =>
            NetworkService.QueuePacket(new CrossbowPullPacket(inventory, slotIndex));
        DispenserWidget.OnDispenserUpdate = (position, data) =>
            NetworkService.QueuePacket(new DispenserPacket(position, data));
        InventorySlotWidget.OnDragDropItem = (sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly) =>
            NetworkService.QueuePacket(new DragDropItemPacket(sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly));
        InventorySlotWidget.OnMoveItem = (sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count) =>
            NetworkService.QueuePacket(new MoveItemPacket(sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count));
        ViewWidget.OnPlayerDragDrop = (playerData, inventoryId, slotIndex, worldPos, count) =>
            NetworkService.QueuePacket(new PlayerDragDropPacket(playerData, inventoryId, slotIndex, worldPos, count));
        VitalStatsWidget.OnChokeRequest = (health, attacker, amount, cause, ignoreInvulnerability) =>
            NetworkService.QueuePacket(new InjureRequestPacket(health, attacker, amount, cause, ignoreInvulnerability, InjureRequestPacket.RequestInjureType.Choke));
        ComponentClothing.OnRequestSkin = (clothing, skinName) => {
            NetworkService.QueuePacket(new RequestSkinPacket(skinName), Time.PeriodicEvent(1.0, 0.0) && NetworkService.IsClient);
            NetworkService.QueuePacket(new WhoHasSkinPacket(skinName), Time.PeriodicEvent(1.0, 0.0) && NetworkService.IsServer);
        };
        PlayerData.OnPlayerPlaying = (playerData) => {
            NetworkService.Self.State = NetworkState.Playing;
            NetworkService.QueuePacket(new NetworkStateChangePacket(NetworkService.Self.ID, NetworkState.Playing), NetworkService.IsClient);
        };
        ComponentSleep.OnSleepEvent = (sleep, allowManualWakeup) => {
            NetworkService.QueuePacket(new SleepRequestPacket(sleep, allowManualWakeup), NetworkService.IsClient);
            NetworkService.QueuePacket(new SleepStatusPacket(sleep, allowManualWakeup, true), NetworkService.IsServer);
        };
        ComponentSleep.OnWakeUpEvent = (sleep) => {
            NetworkService.QueuePacket(new WakeupRequestPacket(sleep), NetworkService.IsClient);
            NetworkService.QueuePacket(new WakeupStatusPacket(sleep), NetworkService.IsServer);
        };
        ComponentPlayer.OnBodyUpdate = (player, isMainPlayer) => {
            NetworkService.QueuePacket(new PlayerBodyUpdatePacket(player) { Except = NetworkService.GetClientByGUID(player.NetworkGuid) }, NetworkService.IsServer && Time.PeriodicEvent(0.1f, 0.0));
            NetworkService.QueuePacket(new PlayerBodyUpdatePacket(player), isMainPlayer && Time.PeriodicEvent(0.1f, 0.0));
        };
    }
}

