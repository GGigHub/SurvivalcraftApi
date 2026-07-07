int targetEntityId = __TARGET_ENTITY_ID__;
var project = Game.GameManager.Project;
var target = project.FindEntity(targetEntityId)?.FindComponent<Game.ComponentBody>();
var player = project.FindSubsystem<Game.SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);

if (target == null || player == null) {
    return "missing";
}

target.IsGravityEnabled = false;

var body = player.ComponentBody;
body.Position = target.Position + new Engine.Vector3(__OFFSET_X__f, __OFFSET_Y__f, __OFFSET_Z__f);
body.Velocity = new Engine.Vector3(__VELOCITY_X__f, __VELOCITY_Y__f, __VELOCITY_Z__f);
body.Rotation = Engine.Quaternion.Identity;
body.IsGravityEnabled = false;
body.IsSmoothRiseEnabled = false;
body.MaxSmoothRiseHeight = 0f;
body.BodyCollidable = true;

player.Update(0f);

body.m_bodiesCollisionBoxes.Clear();
body.FindBodiesCollisionBoxes(body.Position, body.m_bodiesCollisionBoxes);
body.MoveWithCollision(__DT__f, new Engine.Vector3(__MOVE_X__f, __MOVE_Y__f, __MOVE_Z__f));

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "id={0};pos={1:R},{2:R},{3:R};vel={4:R},{5:R},{6:R}",
    body.Entity.Id,
    body.Position.X,
    body.Position.Y,
    body.Position.Z,
    body.Velocity.X,
    body.Velocity.Y,
    body.Velocity.Z);

