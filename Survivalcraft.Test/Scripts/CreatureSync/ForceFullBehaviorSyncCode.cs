var project = Game.GameManager.Project;
var player = project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.First();
var playerCreature = player.Entity.FindComponent<ComponentCreature>();
var playerBody = player.ComponentBody;

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

void EnsureStanding(ComponentBody body, int? standingValue = null) {
    if (body == null) {
        return;
    }
    body.StandingOnValue = standingValue ?? Terrain.MakeBlockValue(2);
    body.StandingOnVelocity = Engine.Vector3.Zero;
    body.Velocity = Engine.Vector3.Zero;
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

void UpdateModel(ComponentModel model, float dt) {
    var update = model.GetType().GetMethod("Update", new[] { typeof(float) });
    update?.Invoke(model, new object[] { dt });
}

void FreezeUpdateable(object value) {
    if (value is IUpdateable updateable) {
        project.FindSubsystem<SubsystemUpdate>().RemoveUpdateable(updateable);
    }
}

var chaseEntity = project.FindEntity(__CHASE_ID__);
if (chaseEntity != null) {
    var behavior = chaseEntity.FindComponent<ComponentChaseBehavior>();
    var creature = chaseEntity.FindComponent<ComponentCreature>();
    if (behavior != null && creature != null && playerCreature != null) {
        ActivateOnly(behavior);
        EnsureStanding(creature.ComponentBody);
        creature.ComponentBody.Position = playerBody.Position - creature.ComponentBody.Matrix.Forward * 0.75f;
        behavior.PlayIdleSoundWhenStartToChase = false;
        behavior.PlayAngrySoundWhenChasing = false;
        behavior.IsAttack = false;
        behavior.Attack(playerCreature, 30f, 60f, true);
        UpdateBehavior(behavior, 0.1f);
        UpdateBehavior(behavior, 0.1f);
        FreezeUpdateable(behavior);
    }
}

var digInMudEntity = project.FindEntity(__DIG_IN_MUD_ID__);
if (digInMudEntity != null) {
    var behavior = digInMudEntity.FindComponent<ComponentDigInMudBehavior>();
    var creature = digInMudEntity.FindComponent<ComponentCreature>();
    if (behavior != null && creature != null) {
        ActivateOnly(behavior);
        int digInValue = Terrain.MakeBlockValue(behavior.m_digInBlockIndex);
        int x = Terrain.ToCell(creature.ComponentBody.Position.X);
        int y = Terrain.ToCell(creature.ComponentBody.Position.Y - 0.9f);
        int z = Terrain.ToCell(creature.ComponentBody.Position.Z);
        project.FindSubsystem<SubsystemTerrain>().ChangeCell(x, y, z, digInValue);
        EnsureStanding(creature.ComponentBody, digInValue);
        behavior.IsDigIn = false;
        behavior.m_stateMachine.TransitionTo("DigIn");
        UpdateBehavior(behavior, 0.1f);
        SetImportance(behavior, 1000f);
        FreezeUpdateable(behavior);
    }
}

var eatPickableEntity = project.FindEntity(__EAT_PICKABLE_ID__);
if (eatPickableEntity != null) {
    var behavior = eatPickableEntity.FindComponent<ComponentEatPickableBehavior>();
    var creature = eatPickableEntity.FindComponent<ComponentCreature>();
    if (behavior != null && creature != null) {
        ActivateOnly(behavior);
        EnsureStanding(creature.ComponentBody);
        var pickables = project.FindSubsystem<SubsystemPickables>();
        behavior.m_pickable = pickables.CreatePickable(Terrain.MakeBlockValue(62), 1, creature.ComponentBody.Position, null, null, eatPickableEntity);
        behavior.IsFeed = false;
        behavior.m_stateMachine.TransitionTo("Eat");
        UpdateBehavior(behavior, 0.1f);
        SetImportance(behavior, 1000f);
        FreezeUpdateable(behavior);
    }
}

var fishOutOfWaterEntity = project.FindEntity(__FISH_OUT_OF_WATER_ID__);
if (fishOutOfWaterEntity != null) {
    var behavior = fishOutOfWaterEntity.FindComponent<ComponentFishOutOfWaterBehavior>();
    var creature = fishOutOfWaterEntity.FindComponent<ComponentCreature>();
    if (behavior != null && creature != null) {
        ActivateOnly(behavior);
        creature.ComponentBody.ImmersionFactor = 0f;
        behavior.IsBend = false;
        UpdateBehavior(behavior, 0.1f);
        UpdateBehavior(behavior, 0.1f);
        FreezeUpdateable(behavior);
    }
}

var humanRowEntity = project.FindEntity(__HUMAN_ROW_ID__);
if (humanRowEntity != null) {
    var model = humanRowEntity.FindComponent<ComponentHumanModel>();
    if (model != null) {
        model.hasData = false;
        model.RowLeftOrder = true;
        model.RowRightOrder = true;
        UpdateModel(model, 0.1f);
        ComponentHumanModel.OnRow?.Invoke(model);
    }
}

var lookAroundEntity = project.FindEntity(__LOOK_AROUND_ID__);
if (lookAroundEntity != null) {
    var behavior = lookAroundEntity.FindComponent<ComponentLookAroundBehavior>();
    var creature = lookAroundEntity.FindComponent<ComponentCreature>();
    if (behavior != null && creature != null) {
        ActivateOnly(behavior);
        EnsureStanding(creature.ComponentBody);
        UpdateBehavior(behavior, 0.1f);
        UpdateBehavior(behavior, 0.1f);
        FreezeUpdateable(behavior);
    }
}

var randomFeedEntity = project.FindEntity(__RANDOM_FEED_ID__);
if (randomFeedEntity != null) {
    var behavior = randomFeedEntity.FindComponent<ComponentRandomFeedBehavior>();
    var creature = randomFeedEntity.FindComponent<ComponentCreature>();
    if (behavior != null && creature != null) {
        ActivateOnly(behavior);
        EnsureStanding(creature.ComponentBody);
        behavior.IsFeed = false;
        behavior.m_stateMachine.TransitionTo("Feed");
        UpdateBehavior(behavior, 0.1f);
        SetImportance(behavior, 1000f);
        FreezeUpdateable(behavior);
    }
}

var randomPeckEntity = project.FindEntity(__RANDOM_PECK_ID__);
if (randomPeckEntity != null) {
    var behavior = randomPeckEntity.FindComponent<ComponentRandomPeckBehavior>();
    var creature = randomPeckEntity.FindComponent<ComponentCreature>();
    if (behavior != null && creature != null) {
        ActivateOnly(behavior);
        EnsureStanding(creature.ComponentBody);
        behavior.IsFeed = false;
        behavior.m_stateMachine.TransitionTo("Peck");
        UpdateBehavior(behavior, 0.1f);
        SetImportance(behavior, 1000f);
        FreezeUpdateable(behavior);
    }
}

var stubbornSteedEntity = project.FindEntity(__STUBBORN_STEED_ID__);
if (stubbornSteedEntity != null) {
    var behavior = stubbornSteedEntity.FindComponent<ComponentStubbornSteedBehavior>();
    var steedBehavior = stubbornSteedEntity.FindComponent<ComponentSteedBehavior>();
    if (behavior != null && steedBehavior != null) {
        ActivateOnly(behavior);
        steedBehavior.WasOrderIssued = true;
        UpdateBehavior(behavior, 0.1f);
        steedBehavior.WasOrderIssued = true;
        UpdateBehavior(behavior, 0.1f);
        FreezeUpdateable(behavior);
    }
}

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
        case "HumanRow": {
            var model = entity.FindComponent<ComponentHumanModel>();
            return model == null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:missing_human_model", name, id)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:left={2},right={3}", name, id, Flag(model.m_rowLeft), Flag(model.m_rowRight));
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
    GetState("HumanRow", __HUMAN_ROW_ID__),
    GetState("LookAround", __LOOK_AROUND_ID__),
    GetState("RandomFeed", __RANDOM_FEED_ID__),
    GetState("RandomPeck", __RANDOM_PECK_ID__),
    GetState("StubbornSteed", __STUBBORN_STEED_ID__)
});
