System.Reflection.FieldInfo FindField(System.Type type, string name) {
    for (var current = type; current != null; current = current.BaseType) {
        var field = current.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null) {
            return field;
        }
    }
    return null;
}

var allBehaviorTypes = typeof(ComponentBehavior).Assembly.GetTypes()
    .Where(t => typeof(ComponentBehavior).IsAssignableFrom(t) && !t.IsAbstract)
    .Where(t => FindField(t, "m_componentPathfinding") != null)
    .ToList();

var project = Game.GameManager.Project;
var templates = project.GameDatabase.Database.Root.GetExplicitNestingChildren(project.GameDatabase.EntityTemplateType, false);

var behaviorsToSpawn = new List<(System.Type BehaviorType, string TemplateName)>();

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
        var category = creatureVd.GetValue<CreatureCategory>("Category", 0);
        if (!MathUtils.IsPowerOf2((long)category)) {
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

var player = project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.First();
var playerBody = player.ComponentBody;
var spawn = project.FindSubsystem<SubsystemCreatureSpawn>();
var terrain = project.FindSubsystem<SubsystemTerrain>().Terrain;

void SetImportance(ComponentBehavior behavior, float value) {
    var field = FindField(behavior.GetType(), "m_importanceLevel");
    if (field != null && field.FieldType == typeof(float)) {
        field.SetValue(behavior, value);
    }
}

var assigned = new List<(int Id, ComponentBody Body, ComponentBehavior Behavior)>();

for (int i = 0; i < behaviorsToSpawn.Count; i++) {
    var pair = behaviorsToSpawn[i];
    var position = playerBody.Position + new Engine.Vector3(3f + 1.5f * i, 0f, 3f);
    int x = Terrain.ToCell(position.X);
    int z = Terrain.ToCell(position.Z);
    position.Y = System.Math.Max(terrain.GetTopHeight(x, z), 150f) + 1.1f;
    
    var entity = spawn.SpawnCreature(pair.TemplateName, position, false);
    var creature = entity?.FindComponent<ComponentCreature>();
    if (creature == null) continue;

    var body = creature.ComponentBody;
    var selector = creature.Entity.FindComponent<ComponentBehaviorSelector>();
    if (selector == null) continue;

    var behavior = creature.Entity.FindComponents<ComponentBehavior>().FirstOrDefault(b => b.GetType() == pair.BehaviorType);
    if (behavior == null) continue;

    foreach (var item in creature.Entity.FindComponents<ComponentBehavior>()) {
        item.IsActive = false;
        SetImportance(item, 0f);
    }
    
    selector.m_behaviors.Clear();
    selector.m_behaviors.Add(behavior);
    behavior.IsActive = true;
    SetImportance(behavior, 1000f);

    var pathfinding = (ComponentPathfinding)FindField(behavior.GetType(), "m_componentPathfinding").GetValue(behavior);
    if (pathfinding == null) {
        pathfinding = creature.Entity.FindComponent<ComponentPathfinding>();
        FindField(behavior.GetType(), "m_componentPathfinding").SetValue(behavior, pathfinding);
    }

    var destination = body.Position + new Engine.Vector3(3f + assigned.Count, 0f, 3f + assigned.Count);
    destination.Y = body.Position.Y;
    if (pathfinding != null) {
        pathfinding.SetDestination(destination, 1f, 0.5f, 0, false, true, false, null);
    }

    var update = behavior.GetType().GetMethod("Update", new[] { typeof(float) });
    update?.Invoke(behavior, new object[] { 0.1f });
    
    assigned.Add((creature.Entity.Id, body, behavior));
}

return string.Join(";", assigned.Select(a => string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "{0}:{1:R},{2:R},{3:R}",
    a.Id,
    a.Body.Position.X,
    a.Body.Position.Y,
    a.Body.Position.Z)));
