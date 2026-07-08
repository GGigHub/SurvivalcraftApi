var project = GameManager.Project;
if (project == null) {
    return "missing";
}

var player = project.FindSubsystem<SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);

var body = player?.ComponentBody;
var locomotion = player?.ComponentLocomotion;
if (body == null || locomotion == null) {
    return "missing";
}

body.IsGravityEnabled = false;
body.CrouchFactor = __CROUCH_FACTOR__f;
body.TargetCrouchFactor = __CROUCH_FACTOR__f;
locomotion.m_lookAutoLevelX = false;
locomotion.m_lookAutoLevelY = false;
locomotion.LookOrder = Engine.Vector2.Zero;
locomotion.LookAngles = new Engine.Vector2(__LOOK_X__f, __LOOK_Y__f);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "id={0};look={1:R},{2:R};crouch={3}",
    body.Entity.Id,
    locomotion.LookAngles.X,
    locomotion.LookAngles.Y,
    body.IsCrouching);
