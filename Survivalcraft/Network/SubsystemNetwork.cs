using Game.Network;
using GameEntitySystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Engine;
using TemplatesDatabase;
#pragma warning disable CA1816

namespace Game;

public class SubsystemNetwork : Subsystem {
    private class Binder<T> where T : Component {
        private readonly bool m_isAdding;
        private readonly T[] m_components;

        public Binder(Entity entity, bool isAdding) {
            m_isAdding = isAdding;
            m_components = entity.FindComponents<T>().ToArray();
        }

        public Binder<T> Bind(string eventName, Delegate handler) {
            if (m_components.Length == 0) return this;

            var eventInfo = typeof(T).GetEvent(eventName);
            if (eventInfo == null) return this;

            foreach (var component in m_components) {
                if (m_isAdding) eventInfo.AddEventHandler(component, handler);
                else eventInfo.RemoveEventHandler(component, handler);
            }
            return this;
        }

        public Binder<T> BindMember<TValue>(string memberName, TValue value) {
            if (m_components.Length == 0) return this;

            var fieldInfo = typeof(T).GetField(memberName);
            var propInfo = typeof(T).GetProperty(memberName);
            if (fieldInfo == null && propInfo == null) return this;

            foreach (var component in m_components) {
                object val = null;
                if (m_isAdding) {
                    if (value is Func<T, object> factory) val = factory(component);
                    else val = value;
                }

                if (fieldInfo != null) fieldInfo.SetValue(component, val);
                else propInfo.SetValue(component, val);
            }
            return this;
        }
    }

    private class InstanceBinder<T> {
        private readonly T m_instance;
        private readonly bool m_isAdding;

        public InstanceBinder(T instance, bool isAdding) {
            m_instance = instance;
            m_isAdding = isAdding;
        }

        public InstanceBinder<T> Bind(string eventName, Delegate handler) {
            if (m_instance == null) return this;

            var eventInfo = typeof(T).GetEvent(eventName);
            if (eventInfo == null) return this;

            if (m_isAdding) eventInfo.AddEventHandler(m_instance, handler);
            else eventInfo.RemoveEventHandler(m_instance, handler);
            return this;
        }

        public InstanceBinder<T> BindMember<TValue>(string memberName, TValue value) {
            if (m_instance == null) return this;

            var fieldInfo = typeof(T).GetField(memberName);
            var propInfo = typeof(T).GetProperty(memberName);
            if (fieldInfo == null && propInfo == null) return this;

            object val = m_isAdding ? value : null;
            if (fieldInfo != null) fieldInfo.SetValue(m_instance, val);
            else propInfo.SetValue(m_instance, val);
            return this;
        }
    }

    private class StaticBinder<T> {
        private readonly bool m_isAdding;

        public StaticBinder(bool isAdding) {
            m_isAdding = isAdding;
        }

        public StaticBinder<T> Bind(string eventName, Delegate handler) {
            var eventInfo = typeof(T).GetEvent(eventName, BindingFlags.Static | BindingFlags.Public);
            if (eventInfo == null) return this;

            if (m_isAdding) eventInfo.AddEventHandler(null, handler);
            else eventInfo.RemoveEventHandler(null, handler);
            return this;
        }

        public StaticBinder<T> BindMember<TValue>(string memberName, TValue value) {
            var fieldInfo = typeof(T).GetField(memberName, BindingFlags.Static | BindingFlags.Public);
            var propInfo = typeof(T).GetProperty(memberName, BindingFlags.Static | BindingFlags.Public);
            if (fieldInfo == null && propInfo == null) return this;

            object val = m_isAdding ? value : null;
            if (fieldInfo != null) fieldInfo.SetValue(null, val);
            else propInfo.SetValue(null, val);
            return this;
        }
    }

    public override void OnEntityAdded(Entity entity) => BindEntity(entity, true);
    public override void OnEntityRemoved(Entity entity) => BindEntity(entity, false);

    public override void Load(ValuesDictionary valuesDictionary) => BindProject(true);
    public override void Dispose() => BindProject(false);

    private void BindProject(bool isAdding) {
        new StaticBinder<Attackment>(isAdding)
            .BindMember(nameof(Attackment.IsNetworkMode),() => NetworkManager.IsClient)
            .Bind(nameof(Attackment.OnHitValueParticleSystemAdded), NetworkDamage.OnHitValueParticleSystemAdded);

        new StaticBinder<PlayerData>(isAdding)
            .BindMember(nameof(PlayerData.GetSelfNetworkGuid), () => NetworkManager.Self.NetworkGuid)
            .BindMember(nameof(PlayerData.GetClientByGUID), (Guid guid) => NetworkManager.GetClientByGUID(guid))
            .BindMember(nameof(PlayerData.IsNetworkMode), () => NetworkManager.IsClient)
            .BindMember(nameof(PlayerData.IsServerMode), () => NetworkManager.IsServer)
            .Bind(nameof(PlayerData.OnPlayerPlaying), NetworkPlayer.OnPlayerPlaying)
            .Bind(nameof(PlayerData.OnPlayerRestart), NetworkPlayer.OnPlayerRestart)
            .Bind(nameof(PlayerData.OnPlayerDisconnected), NetworkPlayer.OnPlayerDisconnected);

        new InstanceBinder<SubsystemInventories>(Project.FindSubsystem<SubsystemInventories>(true), isAdding)
            .BindMember(nameof(SubsystemInventories.IsNetworkMode), () => NetworkManager.IsClient);

        new InstanceBinder<SubsystemSignBlockBehavior>(Project.FindSubsystem<SubsystemSignBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemSignBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .BindMember(nameof(SubsystemSignBlockBehavior.IsMainPlayer), (ComponentPlayer p) => p == NetworkManager.MainPlayer)
            .Bind(nameof(SubsystemSignBlockBehavior.OnEditSignRequest), NetworkSign.OnEditSignRequest)
            .Bind(nameof(SubsystemSignBlockBehavior.OnSignDataChanged), NetworkSign.OnSignDataChanged);

        new InstanceBinder<SubsystemPlayers>(Project.FindSubsystem<SubsystemPlayers>(true), isAdding)
            .BindMember(nameof(SubsystemPlayers.IsNetworkMode), () => NetworkManager.IsClient);

        new InstanceBinder<SubsystemTerrain>(Project.FindSubsystem<SubsystemTerrain>(true), isAdding)
            .BindMember(nameof(SubsystemTerrain.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemTerrain.OnTerrainChangeCell), NetworkTerrain.OnTerrainChangeCell);

        var subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
        if (subsystemTerrain != null) {
            new InstanceBinder<TerrainUpdater>(subsystemTerrain.TerrainUpdater, isAdding)
                .BindMember(nameof(TerrainUpdater.IsNetworkMode), () => NetworkManager.IsClient)
                .Bind(nameof(TerrainUpdater.OnTerrainRequestSyncChunks), NetworkTerrain.OnTerrainRequestSyncChunks)
                .Bind(nameof(TerrainUpdater.OnTerrainSyncChunkList), NetworkTerrain.OnTerrainSyncChunkList)
                .Bind(nameof(TerrainUpdater.OnTerrainReplyResult), NetworkTerrain.OnTerrainReplyResult)
                .Bind(nameof(TerrainUpdater.OnTerrainPlayerLocationUpdate), NetworkTerrain.OnTerrainPlayerLocationUpdate);
        }

        new InstanceBinder<SubsystemElectricity>(Project.FindSubsystem<SubsystemElectricity>(true), isAdding)
            .BindMember(nameof(SubsystemElectricity.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemElectricity.OnElectricityPersistentVoltageChanged), NetworkElectricity.OnElectricityPersistentVoltageChanged);

        new InstanceBinder<SubsystemAdjustableDelayGateBlockBehavior>(Project.FindSubsystem<SubsystemAdjustableDelayGateBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemAdjustableDelayGateBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemAdjustableDelayGateBlockBehavior.OnEditItemDelay), NetworkLogicBlock.OnEditAdjustableDelayGateItem)
            .Bind(nameof(SubsystemAdjustableDelayGateBlockBehavior.OnEditBlockDelay), NetworkLogicBlock.OnEditAdjustableDelayGateBlock);

        new InstanceBinder<SubsystemBatteryBlockBehavior>(Project.FindSubsystem<SubsystemBatteryBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemBatteryBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemBatteryBlockBehavior.OnEditItemVoltage), NetworkLogicBlock.OnEditBatteryItem)
            .Bind(nameof(SubsystemBatteryBlockBehavior.OnEditBlockVoltage), NetworkLogicBlock.OnEditBatteryBlock);

        new InstanceBinder<SubsystemButtonBlockBehavior>(Project.FindSubsystem<SubsystemButtonBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemButtonBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemButtonBlockBehavior.OnEditItemVoltage), NetworkLogicBlock.OnEditButtonItem)
            .Bind(nameof(SubsystemButtonBlockBehavior.OnEditBlockVoltage), NetworkLogicBlock.OnEditButtonBlock);

        new InstanceBinder<SubsystemMemoryBankBlockBehavior>(Project.FindSubsystem<SubsystemMemoryBankBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemMemoryBankBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemMemoryBankBlockBehavior.OnEditItemData), NetworkLogicBlock.OnEditMemoryBankItem)
            .Bind(nameof(SubsystemMemoryBankBlockBehavior.OnEditBlockData), NetworkLogicBlock.OnEditMemoryBankBlock);

        new InstanceBinder<SubsystemPistonBlockBehavior>(Project.FindSubsystem<SubsystemPistonBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemPistonBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemPistonBlockBehavior.OnEditItemData), NetworkLogicBlock.OnEditPistonItem)
            .Bind(nameof(SubsystemPistonBlockBehavior.OnEditBlockData), NetworkLogicBlock.OnEditPistonBlock);

        new InstanceBinder<SubsystemSwitchBlockBehavior>(Project.FindSubsystem<SubsystemSwitchBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemSwitchBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemSwitchBlockBehavior.OnEditItemVoltage), NetworkLogicBlock.OnEditSwitchItem)
            .Bind(nameof(SubsystemSwitchBlockBehavior.OnEditBlockVoltage), NetworkLogicBlock.OnEditSwitchBlock);

        new InstanceBinder<SubsystemTruthTableCircuitBlockBehavior>(Project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemTruthTableCircuitBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(SubsystemTruthTableCircuitBlockBehavior.OnEditItemData), NetworkLogicBlock.OnEditTruthTableCircuitItem)
            .Bind(nameof(SubsystemTruthTableCircuitBlockBehavior.OnEditBlockData), NetworkLogicBlock.OnEditTruthTableCircuitBlock);

        new InstanceBinder<SubsystemChestBlockBehavior>(Project.FindSubsystem<SubsystemChestBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemChestBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .BindMember(nameof(SubsystemChestBlockBehavior.IsMainPlayer), (ComponentPlayer p) => p == NetworkManager.MainPlayer)
            .Bind(nameof(SubsystemChestBlockBehavior.OnOpenInventoryRequest), NetworkEntityBlockBehavior.OnOpenInventoryRequest);

        new InstanceBinder<SubsystemCraftingTableBlockBehavior>(Project.FindSubsystem<SubsystemCraftingTableBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemCraftingTableBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .BindMember(nameof(SubsystemCraftingTableBlockBehavior.IsMainPlayer), (ComponentPlayer p) => p == NetworkManager.MainPlayer)
            .Bind(nameof(SubsystemCraftingTableBlockBehavior.OnOpenInventoryRequest), NetworkEntityBlockBehavior.OnOpenInventoryRequest);

        new InstanceBinder<SubsystemDispenserBlockBehavior>(Project.FindSubsystem<SubsystemDispenserBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemDispenserBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .BindMember(nameof(SubsystemDispenserBlockBehavior.IsMainPlayer), (ComponentPlayer p) => p == NetworkManager.MainPlayer)
            .Bind(nameof(SubsystemDispenserBlockBehavior.OnOpenInventoryRequest), NetworkEntityBlockBehavior.OnOpenInventoryRequest);

        new InstanceBinder<SubsystemFurnaceBlockBehavior>(Project.FindSubsystem<SubsystemFurnaceBlockBehavior>(true), isAdding)
            .BindMember(nameof(SubsystemFurnaceBlockBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .BindMember(nameof(SubsystemFurnaceBlockBehavior.IsMainPlayer), (ComponentPlayer p) => p == NetworkManager.MainPlayer)
            .Bind(nameof(SubsystemFurnaceBlockBehavior.OnOpenInventoryRequest), NetworkEntityBlockBehavior.OnOpenInventoryRequest)
            .Bind(nameof(SubsystemFurnaceBlockBehavior.OnFurnacesUpdate), NetworkFurnace.OnFurnacesUpdate);
    }

    private void BindEntity(Entity entity, bool isAdding) {
        new StaticBinder<ComponentPlayer>(isAdding)
            .BindMember(nameof(ComponentPlayer.GetSelfNetworkGuid), () => NetworkManager.Self.NetworkGuid);

        new Binder<ComponentBody>(entity, isAdding)
            .BindMember(nameof(ComponentBody.IsNetworkMode), (ComponentBody b) => () => NetworkBody.ShouldBeNetworkControlled(b));

        new Binder<ComponentLocomotion>(entity, isAdding)
            .BindMember(nameof(ComponentLocomotion.IsNetworkMode), (ComponentLocomotion l) => () => NetworkBody.ShouldBeNetworkControlled(l.m_componentCreature.ComponentBody));

        new Binder<ComponentBehaviorSelector>(entity, isAdding)
            .BindMember(nameof(ComponentBehaviorSelector.IsNetworkMode), () => NetworkManager.IsClient);

        new Binder<ComponentDamage>(entity, isAdding)
            .BindMember(nameof(ComponentDamage.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentDamage.OnDamage), (ComponentDamage d) => NetworkDamage.OnDamage(d, 0f));

        new Binder<ComponentOnFire>(entity, isAdding)
            .BindMember(nameof(ComponentOnFire.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentOnFire.OnSetOnFire), NetworkOnFire.OnSetOnFire);

        new Binder<ComponentHealth>(entity, isAdding)
            .Bind(nameof(ComponentHealth.OnHealthInjured), NetworkHealth.OnHealthInjured)
            .Bind(nameof(ComponentHealth.OnHealthDead),NetworkHealth.OnHealthDead)
            .Bind(nameof(ComponentHealth.OnHealthHealUpdate), NetworkHealth.OnHealthHealUpdate)
            .Bind(nameof(ComponentHealth.OnHealthFallDamage), NetworkHealth.OnHealthFallDamage);

        new Binder<ComponentFlu>(entity, isAdding)
            .BindMember(nameof(ComponentFlu.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentFlu.OnFluUpdate),NetworkFlu.OnFluUpdate)
            .Bind(nameof(ComponentFlu.OnFluEffectEvent), NetworkFlu.OnFluEffectEvent)
            .Bind(nameof(ComponentFlu.OnStartFluEvent), NetworkFlu.OnStartFluEvent)
            .Bind(nameof(ComponentFlu.OnSneezeEvent), NetworkFlu.OnSneezeEvent);

        new Binder<ComponentSickness>(entity, isAdding)
            .BindMember(nameof(ComponentSickness.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentSickness.OnSicknessUpdate), NetworkSickness.OnSicknessSync);

        new Binder<ComponentSleep>(entity, isAdding)
            .BindMember(nameof(ComponentSleep.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentSleep.OnSleepEvent), NetworkSleep.OnSleepEvent)
            .Bind(nameof(ComponentSleep.OnWakeUpEvent), NetworkSleep.OnWakeUpEvent);

        new Binder<ComponentVitalStats>(entity, isAdding)
            .Bind(nameof(ComponentVitalStats.OnVitalStatsUpdate), NetworkVitalStats.OnVitalStatsSync);

        new Binder<ComponentChaseBehavior>(entity, isAdding)
            .BindMember(nameof(ComponentChaseBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentChaseBehavior.OnIsAttackChanged), NetworkBehavior.OnChaseBehaviorAttackChanged);

        new Binder<ComponentDigInMudBehavior>(entity, isAdding)
            .BindMember(nameof(ComponentDigInMudBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentDigInMudBehavior.OnIsDigInChanged), NetworkBehavior.OnDigInMudBehaviorChanged);

        new Binder<ComponentEatPickableBehavior>(entity, isAdding)
            .BindMember(nameof(ComponentEatPickableBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentEatPickableBehavior.OnIsFeedChanged), NetworkBehavior.OnEatPickableBehaviorChanged);

        new Binder<ComponentFishOutOfWaterBehavior>(entity, isAdding)
            .BindMember(nameof(ComponentFishOutOfWaterBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentFishOutOfWaterBehavior.OnIsBendChanged), NetworkBehavior.OnFishOutOfWaterBehaviorChanged);

        new Binder<ComponentRandomFeedBehavior>(entity, isAdding)
            .BindMember(nameof(ComponentRandomFeedBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentRandomFeedBehavior.OnIsFeedChanged), NetworkBehavior.OnRandomFeedBehaviorChanged);

        new Binder<ComponentRandomPeckBehavior>(entity, isAdding)
            .BindMember(nameof(ComponentRandomPeckBehavior.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentRandomPeckBehavior.OnIsFeedChanged), NetworkBehavior.OnRandomPeckBehaviorChanged);

        new Binder<ComponentCreatureSounds>(entity, isAdding)
            .BindMember(nameof(ComponentCreatureSounds.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentCreatureSounds.OnPlaySound), NetworkCreatureSounds.OnPlaySound);

        new Binder<ComponentFurnace>(entity, isAdding)
            .Bind(nameof(ComponentFurnace.OnFurnaceUpdate), NetworkFurnace.OnFurnaceUpdate);

        new Binder<ComponentLevel>(entity, isAdding)
            .BindMember(nameof(ComponentLevel.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentLevel.OnAddExperience), NetworkLevel.OnAddExperience);

        new Binder<ComponentRider>(entity, isAdding)
            .BindMember(nameof(ComponentRider.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentRider.OnMounting), NetworkRider.OnMounting)
            .Bind(nameof(ComponentRider.OnDismounting), NetworkRider.OnDismounting);

        new Binder<ComponentHumanModel>(entity, isAdding)
            .Bind(nameof(ComponentHumanModel.OnRow), NetworkHumanModel.OnRow);

        new Binder<ComponentClothing>(entity, isAdding)
            .Bind(nameof(ComponentClothing.OnRequestSkin), NetworkClothing.OnRequestSkin);

        new Binder<ComponentPickableGatherer>(entity, isAdding)
            .Bind(nameof(ComponentPickableGatherer.OnPickableAttract), NetworkPickable.OnPickableAttract);

        new Binder<ComponentGui>(entity, isAdding)
            .BindMember(nameof(ComponentGui.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentGui.OnPlayerCreativeFly), NetworkGui.OnPlayerCreativeFly)
            .Bind(nameof(ComponentGui.OnManualLightingStrike), NetworkGui.OnManualLightingStrike)
            .Bind(nameof(ComponentGui.OnManualWeatherUpdate), NetworkGui.OnManualWeatherUpdate)
            .Bind(nameof(ComponentGui.OnManualTimeUpdate), NetworkGui.OnManualTimeUpdate);

        new Binder<ComponentPlayer>(entity, isAdding)
            .BindMember(nameof(ComponentPlayer.IsNetworkMode), () => NetworkManager.IsClient)
            .Bind(nameof(ComponentPlayer.OnActiveSlotChanged), NetworkPlayer.OnActiveSlotChanged)
            .Bind(nameof(ComponentPlayer.OnBodyUpdate), NetworkPlayer.OnBodyUpdate)
            .Bind(nameof(ComponentPlayer.OnHit), NetworkPlayer.OnHit)
            .Bind(nameof(ComponentPlayer.OnDigEvent), NetworkPlayer.OnDigEvent)
            .Bind(nameof(ComponentPlayer.OnInteractEvent), NetworkPlayer.OnInteractEvent)
            .Bind(nameof(ComponentPlayer.OnAimEvent), NetworkPlayer.OnAimEvent);

        new Binder<ComponentInventoryBase>(entity, isAdding)
            .Bind(nameof(ComponentInventoryBase.OnSlotChanged), NetworkInventory.OnSlotChanged);
    }
}
