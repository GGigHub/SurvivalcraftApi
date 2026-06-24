void UpdateBehavior(ComponentBehavior behavior, float dt) {
    var update = behavior.GetType().GetMethod("Update", new[] { typeof(float) });
    update?.Invoke(behavior, new object[] { dt });
}

string GetOrders(string name, int id) {
    var entity = Game.GameManager.Project.FindEntity(id);
    if (entity == null) {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_entity", name, id);
    }

    var model = entity.FindComponent<ComponentCreatureModel>();
    if (model == null) {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_model", name, id);
    }

    float? bendOrder = null;
    if (name == "FishOutOfWater") {
        var behavior = entity.FindComponent<ComponentFishOutOfWaterBehavior>();
        if (behavior != null) {
            if (!NetworkManager.IsNetworkMode) {
                behavior.IsBend = true;
            }
            UpdateBehavior(behavior, 0.1f);
            UpdateBehavior(behavior, 0.1f);
        }
        bendOrder = entity.FindComponent<ComponentFishModel>()?.BendOrder;
    }
    else if (name == "LookAround") {
        var behavior = entity.FindComponent<ComponentLookAroundBehavior>();
        if (behavior != null) {
            UpdateBehavior(behavior, 0.1f);
        }
    }

    return string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "{0}:{1}:bend={2},look={3},shake={4:R}",
        name,
        id,
        bendOrder.HasValue ? 1 : 0,
        model.LookRandomOrder ? 1 : 0,
        model.HeadShakeOrder);
}

return string.Join(";", new[] {
    GetOrders("FishOutOfWater", __FISH_ID__),
    GetOrders("LookAround", __LOOK_AROUND_ID__),
    GetOrders("StubbornSteed", __STUBBORN_STEED_ID__)
});
