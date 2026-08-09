using GameEntitySystem;

namespace Game.Network;

public static class NetworkEntityStateBaselineFactory {
    public static IEnumerable<Packet> Create(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Id <= 0) yield break;

        ComponentPlayer player = entity.FindComponent<ComponentPlayer>();
        if (player != null) {
            yield return PlayerCreativeFlyPacket.CreateState(
                player,
                player.ComponentLocomotion.IsCreativeFlyEnabled);
            yield return new PlayerAddExperiencePacket(
                player.PlayerData,
                count: 0,
                playSound: false,
                player.PlayerData.Level);
            yield return new PlayerStatSyncPacket(player);
        }

        ComponentHealth health = entity.FindComponent<ComponentHealth>();
        if (health != null) yield return new HealthSyncPacket(health);

        ComponentDamage damage = entity.FindComponent<ComponentDamage>();
        if (damage != null) yield return new DamageSyncPacket(damage);

        ComponentVitalStats vitalStats = entity.FindComponent<ComponentVitalStats>();
        if (vitalStats != null) yield return new VitalStatSyncPacket(vitalStats);

        ComponentFlu flu = entity.FindComponent<ComponentFlu>();
        if (flu != null) {
            yield return new ComponentFluPacket(
                flu,
                ComponentFluPacket.EventType.SyncStat);
        }

        ComponentSickness sickness = entity.FindComponent<ComponentSickness>();
        if (sickness != null) {
            yield return new ComponentSicknessPacket(
                sickness,
                ComponentSicknessPacket.EventType.SyncStat);
        }

        ComponentSleep sleep = entity.FindComponent<ComponentSleep>();
        if (sleep != null) {
            yield return new SleepStatusPacket(
                sleep,
                sleep.m_allowManualWakeUp,
                true);
        }

        ComponentOnFire onFire = entity.FindComponent<ComponentOnFire>();
        if (onFire != null) yield return new EntityOnFirePacket(onFire);

        ComponentRider rider = entity.FindComponent<ComponentRider>();
        if (rider != null) {
            yield return ComponentMountPacket.CreateState(
                rider,
                rider.AuthoritativeMount);
        }

        foreach (IInventory inventory in entity.FindComponents<IInventory>()) {
            if (inventory is ComponentClothing clothing) {
                yield return new ClothingSyncPacket(clothing);
            }
            else {
                yield return new InventorySyncPacket(inventory);
            }
        }

        ComponentBody body = entity.FindComponent<ComponentBody>();
        if (body != null && BehaviorSyncPacket.ShouldSyncBehavior(body)) {
            yield return new BehaviorSyncPacket([body]);
        }

        ComponentChaseBehavior chase = entity.FindComponent<ComponentChaseBehavior>();
        if (chase != null) yield return new ChaseBehaviorPacket(chase, chase.IsAttack);

        ComponentDigInMudBehavior digInMud = entity.FindComponent<ComponentDigInMudBehavior>();
        if (digInMud != null) {
            yield return new DigInMudBehaviorPacket(digInMud, digInMud.IsDigIn);
        }

        ComponentEatPickableBehavior eat = entity.FindComponent<ComponentEatPickableBehavior>();
        if (eat != null) yield return new EatPickableBehaviorPacket(eat, eat.IsFeed);

        ComponentFishOutOfWaterBehavior fish =
            entity.FindComponent<ComponentFishOutOfWaterBehavior>();
        if (fish != null) {
            yield return new FishOutOfWaterBehaviorPacket(fish, fish.IsBend);
        }

        ComponentLookAroundBehavior look = entity.FindComponent<ComponentLookAroundBehavior>();
        if (look != null) {
            yield return new LookAroundBehaviorPacket(look, look.IsLookRandom);
        }

        ComponentRandomFeedBehavior feed = entity.FindComponent<ComponentRandomFeedBehavior>();
        if (feed != null) yield return new RandomFeedBehaviorPacket(feed, feed.IsFeed);

        ComponentRandomPeckBehavior peck = entity.FindComponent<ComponentRandomPeckBehavior>();
        if (peck != null) yield return new RandomPeckBehaviorPacket(peck, peck.IsFeed);

        ComponentStubbornSteedBehavior stubborn =
            entity.FindComponent<ComponentStubbornSteedBehavior>();
        ComponentCreatureModel creatureModel = entity.FindComponent<ComponentCreatureModel>();
        if (stubborn != null && creatureModel != null) {
            yield return new StubbornSteedBehaviorPacket(
                stubborn,
                creatureModel.HeadShakeOrder);
        }

        ComponentHumanModel humanModel = entity.FindComponent<ComponentHumanModel>();
        if (humanModel != null && (humanModel.m_rowLeft || humanModel.m_rowRight)) {
            yield return new HumanRowBehaviorPacket(humanModel);
        }
    }

    public static void QueueForSession(Entity entity, ClientSession session) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(session);
        foreach (Packet packet in Create(entity)) {
            packet.To = session;
            NetworkManager.Queue(packet);
        }
    }
}
