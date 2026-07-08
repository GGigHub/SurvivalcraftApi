var project = Game.GameManager.Project;
var center = new Engine.Vector3(__X__ + 0.5f, __Y__ + 0.5f, __Z__ + 0.5f);
int count = 0;
foreach (var entity in project.Entities) {
    if (entity.ValuesDictionary.DatabaseObject.Name != "Boat") {
        continue;
    }
    var frame = entity.FindComponent<Game.ComponentFrame>();
    var body = entity.FindComponent<Game.ComponentBody>();
    Engine.Vector3 position = body?.Position ?? frame?.Position ?? Engine.Vector3.Zero;
    if (Engine.Vector3.DistanceSquared(position, center) <= 16f) {
        count++;
    }
}
return count;
