string result = "";
try {
System.Reflection.FieldInfo FindField(System.Type type, string name) {
    for (var current = type; current != null; current = current.BaseType) {
        var field = current.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null) {
            return field;
        }
    }
    return null;
}

var excluded = new System.Collections.Generic.HashSet<string> {
    "ComponentDigInMudBehavior",
    "ComponentFishOutOfWaterBehavior",
    "ComponentSwimAroundBehavior",
    "ComponentSwimAwayBehavior"
};

var allBehaviorTypes = typeof(Game.ComponentBehavior).Assembly.GetTypes()
    .Where(t => typeof(Game.ComponentBehavior).IsAssignableFrom(t) && !t.IsAbstract)
    .Where(t => !excluded.Contains(t.Name))
    .ToList();

var project = Game.GameManager.Project;
var templates = project.GameDatabase.Database.Root.GetExplicitNestingChildren(project.GameDatabase.EntityTemplateType, false);

var behaviorsToSpawn = new System.Collections.Generic.List<(System.Type BehaviorType, string TemplateName)>();

foreach (var type in allBehaviorTypes) {
    string foundTemplate = null;
    foreach (var template in templates) {
        if (templates.Any(t => t != template && t.EffectivelyInheritsFrom(template))) {
            continue;
        }
        var vd = new TemplatesDatabase.ValuesDictionary();
        vd.PopulateFromDatabaseObject(template);
        if (!vd.TryGetValue<TemplatesDatabase.ValuesDictionary>("Creature", out var creatureVd)) {
            continue;
        }
        foreach (var value in vd.Values) {
            if (value is TemplatesDatabase.ValuesDictionary childVd) {
                string className = childVd.GetValue<string>("Class", "");
                if (className == type.FullName || className == type.Name) {
                    foundTemplate = template.Name;
                    break;
                }
            }
        }
        if (foundTemplate != null) break;
    }
    
    if (foundTemplate != null) {
        behaviorsToSpawn.Add((type, foundTemplate));
    }
}

var player = project.FindSubsystem<Game.SubsystemPlayers>().ComponentPlayers.First();
var playerBody = player.ComponentBody;
var spawn = project.FindSubsystem<Game.SubsystemCreatureSpawn>();
var terrain = project.FindSubsystem<Game.SubsystemTerrain>().Terrain;

void SetImportance(Game.ComponentBehavior behavior, float value) {
    var field = FindField(behavior.GetType(), "m_importanceLevel");
    if (field != null && field.FieldType == typeof(float)) {
        field.SetValue(behavior, value);
    }
}

var assigned = new System.Collections.Generic.List<(int Id, Game.ComponentBody Body, Game.ComponentBehavior Behavior)>();

for (int i = 0; i < behaviorsToSpawn.Count; i++) {
    var pair = behaviorsToSpawn[i];
    var position = playerBody.Position + new Engine.Vector3(4f + 2f * (i % 5), 0f, 4f + 2f * (i / 5));
    int x = Game.Terrain.ToCell(position.X);
    int z = Game.Terrain.ToCell(position.Z);
    float topHeight = terrain.GetTopHeight(x, z);
    position.Y = (topHeight > 0 ? topHeight : 70f) + 1.1f;
    
    var entity = spawn.SpawnCreature(pair.TemplateName, position, false);
    var creature = entity?.FindComponent<Game.ComponentCreature>();
    if (creature == null) continue;

    var health = entity.FindComponent<Game.ComponentHealth>();
    if (health != null) {
        health.Health = 100000f;
        health.IsInvulnerable = true;
    }

    var despawn = entity.FindComponent<Game.ComponentSpawn>();
    if (despawn != null) {
        despawn.AutoDespawn = false;
    }

    var body = creature.ComponentBody;
    body.IsGravityEnabled = false;
    body.Velocity = Engine.Vector3.Zero;

    var selector = creature.Entity.FindComponent<Game.ComponentBehaviorSelector>();
    if (selector == null) continue;

    var behavior = creature.Entity.FindComponents<Game.ComponentBehavior>().FirstOrDefault(b => b.GetType() == pair.BehaviorType);
    if (behavior == null) continue;

    foreach (var item in creature.Entity.FindComponents<Game.ComponentBehavior>()) {
        item.IsActive = false;
        SetImportance(item, 0f);
    }
    
    selector.m_behaviors.Clear();
    selector.m_behaviors.Add(behavior);
    behavior.IsActive = true;
    SetImportance(behavior, 1000f);

    try {
        var update = behavior.GetType().GetMethod("Update", new[] { typeof(float) });
        update?.Invoke(behavior, new object[] { 0.1f });
    } catch (System.Exception ex) {
        result = "failed_updating_" + pair.BehaviorType.Name + ":" + (ex.InnerException?.Message ?? ex.Message);
        break;
    }
    
    assigned.Add((creature.Entity.Id, body, behavior));
}

    if (string.IsNullOrEmpty(result)) {
        if (assigned.Count == 0) {
            result = $"DEBUG: all={allBehaviorTypes.Count}, spawn={behaviorsToSpawn.Count}";
        } else {
            result = string.Join(";", assigned.Select(a => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}:{1}",
                a.Behavior.GetType().Name,
                a.Id)));
        }
    }
} catch (System.Exception ex) {
    result = "failed_global:" + ex.Message + "\n" + ex.StackTrace;
}
result
