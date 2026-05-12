using Game.Network;
using GameEntitySystem;
using System;
using System.Linq;
using Engine;

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
    }

    public override void OnEntityAdded(Entity entity) => BindEntity(entity, true);
    public override void OnEntityRemoved(Entity entity) => BindEntity(entity, false);

    private void BindEntity(Entity entity, bool isAdding) {
        new Binder<ComponentHealth>(entity, isAdding)
            .Bind(nameof(ComponentHealth.OnHealthInjured), NetworkHealth.OnHealthInjured)
            .Bind(nameof(ComponentHealth.OnHealthDead), NetworkHealth.OnHealthDead)
            .Bind(nameof(ComponentHealth.OnHealthHealUpdate), NetworkHealth.OnHealthHealUpdate)
            .Bind(nameof(ComponentHealth.OnHealthFallDamage), NetworkHealth.OnHealthFallDamage);

        new Binder<ComponentDamage>(entity, isAdding)
            .Bind(nameof(ComponentDamage.OnDamage), NetworkDamage.OnDamage);

        new Binder<ComponentOnFire>(entity, isAdding)
            .Bind(nameof(ComponentOnFire.OnSetOnFire), NetworkOnFire.OnSetOnFire);

        new Binder<ComponentFlu>(entity, isAdding)
            .Bind(nameof(ComponentFlu.OnFluUpdate), NetworkFlu.OnFluUpdate)
            .Bind(nameof(ComponentFlu.OnFluEffectEvent), NetworkFlu.OnFluEffectEvent)
            .Bind(nameof(ComponentFlu.OnStartFluEvent), NetworkFlu.OnStartFluEvent)
            .Bind(nameof(ComponentFlu.OnSneezeEvent), NetworkFlu.OnSneezeEvent);

        new Binder<ComponentSickness>(entity, isAdding)
            .Bind(nameof(ComponentSickness.OnSicknessUpdate), NetworkSickness.OnSicknessSync);

        new Binder<ComponentSleep>(entity, isAdding)
            .Bind(nameof(ComponentSleep.OnSleepEvent), NetworkSleep.OnSleepEvent)
            .Bind(nameof(ComponentSleep.OnWakeUpEvent), NetworkSleep.OnWakeUpEvent);

        new Binder<ComponentVitalStats>(entity, isAdding)
            .Bind(nameof(ComponentVitalStats.OnVitalStatsUpdate), NetworkVitalStats.OnVitalStatsSync);

        new Binder<ComponentChaseBehavior>(entity, isAdding)
            .Bind(nameof(ComponentChaseBehavior.OnIsAttackChanged), NetworkBehavior.OnChaseBehaviorAttackChanged);

        new Binder<ComponentDigInMudBehavior>(entity, isAdding)
            .Bind(nameof(ComponentDigInMudBehavior.OnIsDigInChanged), NetworkBehavior.OnDigInMudBehaviorChanged);

        new Binder<ComponentEatPickableBehavior>(entity, isAdding)
            .Bind(nameof(ComponentEatPickableBehavior.OnIsFeedChanged), NetworkBehavior.OnEatPickableBehaviorChanged);

        new Binder<ComponentFishOutOfWaterBehavior>(entity, isAdding)
            .Bind(nameof(ComponentFishOutOfWaterBehavior.OnIsBendChanged), NetworkBehavior.OnFishOutOfWaterBehaviorChanged);

        new Binder<ComponentRandomFeedBehavior>(entity, isAdding)
            .Bind(nameof(ComponentRandomFeedBehavior.OnIsFeedChanged), NetworkBehavior.OnRandomFeedBehaviorChanged);

        new Binder<ComponentRandomPeckBehavior>(entity, isAdding)
            .Bind(nameof(ComponentRandomPeckBehavior.OnIsFeedChanged), NetworkBehavior.OnRandomPeckBehaviorChanged);

        new Binder<ComponentCreatureSounds>(entity, isAdding)
            .Bind(nameof(ComponentCreatureSounds.OnPlaySound), NetworkCreatureSounds.OnPlaySound);

        new Binder<ComponentFurnace>(entity, isAdding)
            .Bind(nameof(ComponentFurnace.OnFurnaceUpdate), NetworkFurnace.OnFurnaceUpdate);

        new Binder<ComponentLevel>(entity, isAdding)
            .Bind(nameof(ComponentLevel.OnAddExperience), NetworkLevel.OnAddExperience);

        new Binder<ComponentRider>(entity, isAdding)
            .Bind(nameof(ComponentRider.OnMounting), NetworkRider.OnMounting)
            .Bind(nameof(ComponentRider.OnDismounting), NetworkRider.OnDismounting);

        new Binder<ComponentHumanModel>(entity, isAdding)
            .Bind(nameof(ComponentHumanModel.OnRow), NetworkHumanModel.OnRow);

        new Binder<ComponentClothing>(entity, isAdding)
            .Bind(nameof(ComponentClothing.OnRequestSkin), NetworkClothing.OnRequestSkin);

        new Binder<ComponentPickableGatherer>(entity, isAdding)
            .Bind(nameof(ComponentPickableGatherer.OnPickableAttract), NetworkPickable.OnPickableAttract);

        new Binder<ComponentGui>(entity, isAdding)
            .Bind(nameof(ComponentGui.OnPlayerCreativeFly), NetworkGui.OnPlayerCreativeFly)
            .Bind(nameof(ComponentGui.OnManualLightingStrike), NetworkGui.OnManualLightingStrike)
            .Bind(nameof(ComponentGui.OnManualWeatherUpdate), NetworkGui.OnManualWeatherUpdate)
            .Bind(nameof(ComponentGui.OnManualTimeUpdate), NetworkGui.OnManualTimeUpdate);

        new Binder<ComponentPlayer>(entity, isAdding)
            .Bind(nameof(ComponentPlayer.OnActiveSlotChanged), NetworkPlayer.OnActiveSlotChanged)
            .Bind(nameof(ComponentPlayer.OnBodyUpdate), NetworkPlayer.OnBodyUpdate)
            .Bind(nameof(ComponentPlayer.OnHit), NetworkPlayer.OnHit)
            .Bind(nameof(ComponentPlayer.OnDigEvent), NetworkPlayer.OnDigEvent)
            .Bind(nameof(ComponentPlayer.OnInteractEvent), NetworkPlayer.OnInteractEvent)
            .Bind(nameof(ComponentPlayer.OnAimEvent), NetworkPlayer.OnAimEvent);
    }
}
