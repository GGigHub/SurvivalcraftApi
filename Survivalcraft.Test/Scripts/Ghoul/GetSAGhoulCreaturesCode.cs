float centerX = __CENTER_X__f;
float centerY = __CENTER_Y__f;
float centerZ = __CENTER_Z__f;
float radius = __RADIUS__f;
var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var center = new Engine.Vector3(centerX, centerY, centerZ);
var parts = new List<string>();
foreach (var entity in project.Entities.OrderBy(e => e.Id)) {
    var saghoul = entity.FindComponent<SAGhoul.Components.ComponentSAGhouls>();
    if (saghoul == null) {
        continue;
    }
    var body = entity.FindComponent<Game.ComponentBody>();
    if (body != null && Engine.Vector3.DistanceSquared(body.Position, center) > radius * radius) {
        continue;
    }
    parts.Add(string.Join("|",
        "id=" + entity.Id,
        "name=" + entity.ValuesDictionary.DatabaseObject.Name,
        "entityName=" + saghoul.EntityName,
        "isBoss=" + saghoul.IsBoss,
        "isCommonFrank=" + saghoul.IsCommonFrank,
        "persistentAttack=" + saghoul.PersistentAttack,
        "isTough=" + saghoul.IsTough));
}
return string.Join(";", parts);