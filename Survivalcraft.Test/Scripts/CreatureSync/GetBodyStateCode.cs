int entityId = __ENTITY_ID__;
var entity = Game.GameManager.Project.FindEntity(entityId);
if (entity == null) {
    return "missing";
}

var body = entity.FindComponent<Game.ComponentBody>();
if (body == null) {
    return "missing";
}

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "id={0};pos={1:R},{2:R},{3:R};vel={4:R},{5:R},{6:R}",
    entity.Id,
    body.Position.X,
    body.Position.Y,
    body.Position.Z,
    body.Velocity.X,
    body.Velocity.Y,
    body.Velocity.Z);

