string Flag(bool value) => value ? "1" : "0";

string GetState(string name, int id) {
    var entity = Game.GameManager.Project.FindEntity(id);
    if (entity == null) {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_entity", name, id);
    }

    switch (name) {
        case "Chase": {
            var behavior = entity.FindComponent<ComponentChaseBehavior>();
            return behavior == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_chase", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:attack={2}", name, id, Flag(behavior.IsAttack));
        }
        case "DigInMud": {
            var behavior = entity.FindComponent<ComponentDigInMudBehavior>();
            return behavior == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_dig_in_mud", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:dig={2}", name, id, Flag(behavior.IsDigIn));
        }
        case "EatPickable": {
            var behavior = entity.FindComponent<ComponentEatPickableBehavior>();
            return behavior == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_eat_pickable", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:feed={2}", name, id, Flag(behavior.IsFeed));
        }
        case "FishOutOfWater": {
            var behavior = entity.FindComponent<ComponentFishOutOfWaterBehavior>();
            return behavior == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_fish_out_of_water", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:bend={2}", name, id, Flag(behavior.IsBend));
        }
        case "LookAround": {
            var behavior = entity.FindComponent<ComponentLookAroundBehavior>();
            return behavior == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_look_around", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:look={2}", name, id, Flag(behavior.IsLookRandom));
        }
        case "RandomFeed": {
            var behavior = entity.FindComponent<ComponentRandomFeedBehavior>();
            return behavior == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_random_feed", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:feed={2}", name, id, Flag(behavior.IsFeed));
        }
        case "RandomPeck": {
            var behavior = entity.FindComponent<ComponentRandomPeckBehavior>();
            return behavior == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_random_peck", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:feed={2}", name, id, Flag(behavior.IsFeed));
        }
        case "StubbornSteed": {
            var model = entity.FindComponent<ComponentCreatureModel>();
            return model == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_creature_model", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:shake={2:R}", name, id, model.HeadShakeOrder);
        }
        default:
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_unknown", name, id);
    }
}

return string.Join(";", new[] {
    GetState("Chase", __CHASE_ID__),
    GetState("DigInMud", __DIG_IN_MUD_ID__),
    GetState("EatPickable", __EAT_PICKABLE_ID__),
    GetState("FishOutOfWater", __FISH_OUT_OF_WATER_ID__),
    GetState("LookAround", __LOOK_AROUND_ID__),
    GetState("RandomFeed", __RANDOM_FEED_ID__),
    GetState("RandomPeck", __RANDOM_PECK_ID__),
    GetState("StubbornSteed", __STUBBORN_STEED_ID__)
});

