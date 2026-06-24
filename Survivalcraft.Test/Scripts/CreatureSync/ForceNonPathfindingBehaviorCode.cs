System.Reflection.FieldInfo FindField(System.Type type, string name) {
    for (var current = type; current != null; current = current.BaseType) {
        var field = current.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null) {
            return field;
        }
    }
    return null;
}

void SetImportance(ComponentBehavior behavior, float value) {
    var field = FindField(behavior.GetType(), "m_importanceLevel");
    if (field != null && field.FieldType == typeof(float)) {
        field.SetValue(behavior, value);
    }
}

void ActivateOnly(ComponentBehavior behavior) {
    foreach (var item in behavior.Entity.FindComponents<ComponentBehavior>()) {
        item.IsActive = false;
        SetImportance(item, 0f);
    }
    var selector = behavior.Entity.FindComponent<ComponentBehaviorSelector>();
    if (selector != null) {
        selector.m_behaviors.Clear();
        selector.m_behaviors.Add(behavior);
    }
    behavior.IsActive = true;
    SetImportance(behavior, 1000f);
}

void UpdateBehavior(ComponentBehavior behavior, float dt) {
    var update = behavior.GetType().GetMethod("Update", new[] { typeof(float) });
    update?.Invoke(behavior, new object[] { dt });
}

var fishEntity = Game.GameManager.Project.FindEntity(__FISH_ID__);
if (fishEntity != null) {
    var behavior = fishEntity.FindComponent<ComponentFishOutOfWaterBehavior>();
    if (behavior != null) {
        ActivateOnly(behavior);
        behavior.IsBend = false;
        behavior.IsBend = true;
        UpdateBehavior(behavior, 0.1f);
        UpdateBehavior(behavior, 0.1f);
    }
}

var lookEntity = Game.GameManager.Project.FindEntity(__LOOK_AROUND_ID__);
if (lookEntity != null) {
    var behavior = lookEntity.FindComponent<ComponentLookAroundBehavior>();
    if (behavior != null) {
        ActivateOnly(behavior);
        UpdateBehavior(behavior, 0.1f);
        UpdateBehavior(behavior, 0.1f);
    }
}

var stubbornEntity = Game.GameManager.Project.FindEntity(__STUBBORN_STEED_ID__);
if (stubbornEntity != null) {
    var behavior = stubbornEntity.FindComponent<ComponentStubbornSteedBehavior>();
    var steedBehavior = stubbornEntity.FindComponent<ComponentSteedBehavior>();
    if (behavior != null && steedBehavior != null) {
        ActivateOnly(behavior);
        steedBehavior.WasOrderIssued = true;
        UpdateBehavior(behavior, 0.1f);
        steedBehavior.WasOrderIssued = true;
        UpdateBehavior(behavior, 0.1f);
    }
}

return string.Empty;
