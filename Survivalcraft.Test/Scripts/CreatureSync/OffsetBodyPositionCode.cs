int entityId = __ENTITY_ID__;
var entity = Game.GameManager.Project.FindEntity(entityId);
var body = entity?.FindComponent<Game.ComponentBody>();
if (body == null) {
    return "missing";
}

body.IsGravityEnabled = false;
body.Position += new Engine.Vector3(__OFFSET_X__f, __OFFSET_Y__f, __OFFSET_Z__f);
body.Velocity = new Engine.Vector3(__VELOCITY_X__f, __VELOCITY_Y__f, __VELOCITY_Z__f);
body.Rotation = Engine.Quaternion.Identity;

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

