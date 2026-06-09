using System;
using System.Collections.Generic;
using System.Text;
using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class RootManager {
    public static void Initialize() {
        ComponentChaseBehavior.OnIsAttackChanged = (behavior, isAttack) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new ChaseBehaviorPacket(behavior, isAttack));
        };
        ComponentCreatureSounds.OnPlaySound = (sounds, type, skipIfRecentlyPlayed) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new CreatureSoundPacket(sounds, type, skipIfRecentlyPlayed));
        };
        ComponentDamage.OnDamage = (damage) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new DamageSyncPacket(damage));
        };
        ComponentBody.OnBodyUpdate = BodyUpdatePacket.CollectBodyForSync;
        ComponentDigInMudBehavior.OnIsDigInChanged = (behavior, isDigIn) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new DigInMudBehaviorPacket(behavior, isDigIn));
        };
        ComponentEatPickableBehavior.OnIsFeedChanged = (behavior, isFeed) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new EatPickableBehaviorPacket(behavior, isFeed));
        };
        ComponentFishOutOfWaterBehavior.OnIsBendChanged = (behavior, isBend) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new FishOutOfWaterBehaviorPacket(behavior, isBend));
        };
        ComponentFlu.OnFluEffectEvent = (flu) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.FluEffect));
        };
        ComponentFlu.OnFluUpdate = (flu) => {
            if (NetworkService.IsServer && Time.PeriodicEvent(1.0, 0.5))
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat));
        };
        ComponentFlu.OnSneezeEvent = (flu) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.Sneeze));
        };
        ComponentFlu.OnStartFluEvent = (flu) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.StartFlu));
        };
        ComponentFurnace.OnFurnaceUpdate = (furnace) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel));
        };
        ComponentGui.OnManualLightingStrike = (gui, position, direction) =>
            NetworkService.QueuePacket(new SubsystemSkyPacket(position, direction));
        ComponentGui.OnManualTimeUpdate = (gui, totalElapsedGameTime, timeOfDayOffset) =>
            NetworkService.QueuePacket(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        ComponentGui.OnManualWeatherUpdate = (gui, type) =>
            NetworkService.QueuePacket(new SubsystemWeatherPacket(type));
        ComponentGui.OnPlayerCreativeFly = (gui, player) =>
            NetworkService.QueuePacket(new PlayerCreativeFlyPacket(player));
        ComponentHealth.OnHealthDead = (health) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new HealthSyncPacket(health));
        };
        ComponentHealth.OnHealthFallDamage = InjureRequestPacket.SendFallDamage;
        ComponentHealth.OnHealthHealUpdate = (health) => {
            if (NetworkService.IsServer && Time.PeriodicEvent(2.0, 0.0))
                NetworkService.QueuePacket(new HealthSyncPacket(health));
        };
        ComponentHealth.OnHealthInjured = (health, injury, amount) => {
            if (NetworkService.IsServer && amount > 0f)
                NetworkService.QueuePacket(new InjureEventPacket(health, injury.Attacker, amount, injury.Cause, injury.IgnoreInvulnerability));
        };
        ComponentHumanModel.OnRow = (model) =>
            NetworkService.QueuePacket(new HumanRowBehaviorPacket(model));
        ComponentInventoryBase.OnSlotChanged = (inventory, slotIndex) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new InventorySyncPacket(inventory));
        };
        ComponentLevel.OnAddExperience = (level, count, playSound) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new PlayerAddExperiencePacket(level.m_componentPlayer.PlayerData, count, playSound, level.m_componentPlayer.PlayerData.Level));
        };
        ComponentOnFire.OnSetOnFire = (onFire, attacker, duration) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new EntityOnFirePacket(onFire, attacker, duration));
        };
        ComponentPickableGatherer.OnPickableAttract = (pickable) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new PickableSetFlyToPositionPacket(pickable));
        };
        ComponentPlayer.OnActiveSlotChanged = (player) => {
            if (player.ComponentMiner?.Inventory != null)
                NetworkService.QueuePacket(new ActiveSlotChangePacket(player.ComponentMiner.Inventory));
        };
        ComponentPlayer.OnAimEvent = (player) =>
            NetworkService.QueuePacket(new PlayerAimPacket(player));
        ComponentPlayer.OnDigEvent = (player) =>
            NetworkService.QueuePacket(new PlayerDigPacket(player));
        ComponentPlayer.OnHit = (player, hitBody, hitPoint, direction) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new PlayerHitPacket(player, hitBody, hitPoint, direction));
        };
        ComponentPlayer.OnInteractEvent = (player) =>
            NetworkService.QueuePacket(new PlayerInteractPacket(player));
        ComponentRandomFeedBehavior.OnIsFeedChanged = (behavior, isFeed) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new RandomFeedBehaviorPacket(behavior, isFeed));
        };
        ComponentRandomPeckBehavior.OnIsFeedChanged = (behavior, isPeck) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new RandomPeckBehaviorPacket(behavior, isPeck));
        };
        ComponentRider.OnDismounting = (rider, isRequest) =>
            NetworkService.QueuePacket(new ComponentMountPacket(rider, isRequest));
        ComponentRider.OnMounting = (rider, mount, isRequest) =>
            NetworkService.QueuePacket(new ComponentMountPacket(rider, mount, isRequest));
        ComponentSickness.OnSicknessUpdate = (sickness) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new ComponentSicknessPacket(sickness));
        };
        ComponentVitalStats.OnVitalStatsUpdate = (stats) => {
            if (NetworkService.IsServer && Time.PeriodicEvent(1.0, 0.5))
                NetworkService.QueuePacket(new VitalStatSyncPacket(stats));
        };
        SubsystemAdjustableDelayGateBlockBehavior.OnEditBlockDelay = (subsystem, cell, newDelay) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new AdjustableDelayGatePacket(cell, false, default, default, newDelay));
        };
        SubsystemAdjustableDelayGateBlockBehavior.OnEditItemDelay = (subsystem, inventoryId, slotIndex, newDelay) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new AdjustableDelayGatePacket(default, true, inventoryId, slotIndex, newDelay));
        };
        SubsystemBatteryBlockBehavior.OnEditBlockVoltage = (subsystem, cell, newVoltageLevel) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new BatteryVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemBatteryBlockBehavior.OnEditItemVoltage = (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new BatteryVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemButtonBlockBehavior.OnEditBlockVoltage = (subsystem, cell, newVoltageLevel) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new ButtonVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemButtonBlockBehavior.OnEditItemVoltage = (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new ButtonVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemElectricity.OnElectricityPersistentVoltageChanged = (subsystem, list) => {
            if (!NetworkService.IsClient)
                NetworkService.QueuePacket(new SubsystemElectricityPacket(list));
        };
        SubsystemEntityBlockBehavior.OnOpenInventoryRequest = (subsystem, point) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new OpenInventoryByPointPacket(point));
        };
        SubsystemExplosions.OnExplosionCell = (explosionCells) =>
            NetworkService.QueuePacket(new ExplosionCellPacket(explosionCells));
        SubsystemExplosions.OnExplosionSound = (position, pressure, delay) =>
            NetworkService.QueuePacket(new ExplosionSoundPacket(position, pressure, delay));
        SubsystemFireBlockBehavior.OnFireBlockAdd = (x, y, z, expandability) =>
            NetworkService.QueuePacket(new FireBlockAddPacket(x, y, z, expandability));
        SubsystemFireBlockBehavior.OnFireBlockRemove = (x, y, z) =>
            NetworkService.QueuePacket(new FireBlockRemovePacket(x, y, z));
        SubsystemFurnaceBlockBehavior.OnFurnacesUpdate = (subsystem, furnaces) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new ComponentFurnacePacket(furnaces));
        };
        SubsystemGameInfo.OnTimeUpdate = (totalElapsedGameTime, timeOfDayOffset) =>
            NetworkService.QueuePacket(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        SubsystemMemoryBankBlockBehavior.OnEditBlockData = (subsystem, cell, data) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new MemoryBankDataPacket(cell, false, default, default, data));
        };
        SubsystemMemoryBankBlockBehavior.OnEditItemData = (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new MemoryBankDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemMemoryBankBlockBehavior.OnItemDataHarvested = (inventoryId, data) => {
            if (!NetworkService.IsClient)
                NetworkService.QueuePacket(new MemoryBankDataPacket(default, true, inventoryId, -1, data));
        };
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
        SubsystemPistonBlockBehavior.OnEditBlockData = (subsystem, cell, data) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new PistonDataPacket(cell, false, default, default, data));
        };
        SubsystemPistonBlockBehavior.OnEditItemData = (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new PistonDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemProjectiles.OnProjectileFire = (projectile) =>
            NetworkService.QueuePacket(new ProjectilePacket(projectile));
        SubsystemSignBlockBehavior.OnEditSignRequest = (subsystem, point) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new EditSignPacket(point));
        };
        SubsystemSignBlockBehavior.OnSignDataChanged = (subsystem, point, lines, colors, url) =>
            NetworkService.QueuePacket(new SignBlockPacket(point, lines, colors, url));
        SubsystemSky.OnSkyLightning = (targetPosition) =>
            NetworkService.QueuePacket(new SubsystemSkyPacket(targetPosition));
        SubsystemSwitchBlockBehavior.OnEditBlockVoltage = (subsystem, cell, newVoltageLevel) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new SwitchVoltagePacket(cell, false, default, default, newVoltageLevel));
        };
        SubsystemSwitchBlockBehavior.OnEditItemVoltage = (subsystem, inventoryId, slotIndex, newVoltageLevel) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new SwitchVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        };
        SubsystemTerrain.OnTerrainChangeCell = (subsystem, x, y, z, value) => {
            if (!NetworkService.IsClient)
                NetworkService.QueuePacket(new TerrainChangeCellPacket(x, y, z, value));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnEditBlockData = (subsystem, cell, data) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new TruthTableDataPacket(cell, false, default, default, data));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnEditItemData = (subsystem, inventoryId, slotIndex, data) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new TruthTableDataPacket(default, true, inventoryId, slotIndex, data));
        };
        SubsystemTruthTableCircuitBlockBehavior.OnItemDataHarvested = (inventoryId, data) => {
            if (!NetworkService.IsClient)
                NetworkService.QueuePacket(new TruthTableDataPacket(default, true, inventoryId, -1, data));
        };
        SubsystemWeather.OnFogUpdate = (startTime, rampTime, endTime) =>
            NetworkService.QueuePacket(new SubsystemWeatherPacket(startTime, rampTime, endTime));
        SubsystemWeather.OnPrecipitationUpdate = (startTime, endTime, intensity) =>
            NetworkService.QueuePacket(new SubsystemWeatherPacket(startTime, endTime, intensity));
        TerrainUpdater.OnTerrainPlayerLocationUpdate = (updater, location) =>
            NetworkService.QueuePacket(new PlayerUpdateLocationPacket(location));
        TerrainUpdater.OnTerrainReplyResult = (updater, playerGuid, chunks) =>
            NetworkService.QueuePacket(new TerrainReplyResultPacket(chunks) { To = NetworkService.GetClientByGUID(playerGuid) });
        TerrainUpdater.OnTerrainRequestSyncChunks = (updater, chunks) =>
            NetworkService.QueuePacket(new TerrainRequestSyncChunksPacket(chunks));
        TerrainUpdater.OnTerrainSyncChunkList = (updater, playerGuid, chunks) =>
            NetworkService.QueuePacket(new TerrainSyncChunkListPacket(chunks) { To = NetworkService.GetClientByGUID(playerGuid) });
        PlayerData.OnPlayerRestart = (playerData) => {
            if (NetworkService.IsClient && playerData.IsMainPlayer)
                NetworkService.QueuePacket(new PlayerRestartPacket(playerData));
        };
        GameLoadingScreen.OnPlayerIntoPlaying = () =>
            NetworkService.QueuePacket(new PlayerIntoPlayingPacket(NetworkService.MainPlayer));
        PlayerScreen.OnPlayerCreate = (playerData) =>
            NetworkService.QueuePacket(new PlayerCreatePacket(playerData));
        PlayerScreen.OnPlayerModify = (playerData) =>
            NetworkService.QueuePacket(new PlayerModifyPacket(playerData));
        GameLoadingScreen.OnPlayerStateChanged = (state) => {
            NetworkService.Self.NetworkState = state;
            NetworkService.QueuePacket(new StatePacket(NetworkService.Self.PlayerIndex, state));
        };
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
            if (Time.PeriodicEvent(1.0, 0.0) && NetworkService.IsClient)
                NetworkService.QueuePacket(new RequestSkinPacket(skinName));
            if (Time.PeriodicEvent(1.0, 0.0) && NetworkService.IsServer)
                NetworkService.QueuePacket(new WhoHasSkinPacket(skinName));
        };
        PlayerData.OnPlayerPlaying = (playerData) => {
            NetworkService.Self.NetworkState = NetworkState.Playing;
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new StatePacket(NetworkService.Self.PlayerIndex, NetworkState.Playing));
        };
        ComponentSleep.OnSleepEvent = (sleep, allowManualWakeup) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new SleepRequestPacket(sleep, allowManualWakeup));
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new SleepStatusPacket(sleep, allowManualWakeup, true));
        };
        ComponentSleep.OnWakeUpEvent = (sleep) => {
            if (NetworkService.IsClient)
                NetworkService.QueuePacket(new WakeupRequestPacket(sleep));
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new WakeupStatusPacket(sleep));
        };
        ComponentPlayer.OnBodyUpdate = (player, isMainPlayer) => {
            if (NetworkService.IsServer && Time.PeriodicEvent(0.1f, 0.0))
                NetworkService.QueuePacket(new PlayerBodyUpdatePacket(player) { Except = NetworkService.GetClientByGUID(player.NetworkGuid) });
            if (isMainPlayer && Time.PeriodicEvent(0.1f, 0.0))
                NetworkService.QueuePacket(new PlayerBodyUpdatePacket(player));
        };
        PlayerData.PlayerFirstUpdated = (player) => {
            // 非本地主玩家且缺少对应网络客户端时，转为离线状态。
            if (NetworkService.GetClientByGUID(player.NetworkGuid) == null) {
                player.m_stateMachine.TransitionTo("MakePlayerOffline");
            }
        };
        ComponentBody.IsNetworkMode = BodyUpdatePacket.ShouldBeNetworkControlled;
        ComponentLocomotion.IsNetworkMode = BodyUpdatePacket.ShouldBeNetworkControlled;
        Project.EntityAdded += (_, arg) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new EntityLoadPacket(arg.Entity));
        };
        Project.EntityRemoved += (_, arg) => {
            if (NetworkService.IsServer)
                NetworkService.QueuePacket(new EntityRemovePacket(arg.Entity.Id));
        };
    }
}
