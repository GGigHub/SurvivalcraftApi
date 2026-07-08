int playerIndex = __PLAYER_INDEX__;
var project = GameManager.Project;
if (project == null) {
    return "missing";
}

var player = project.FindSubsystem<SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.PlayerIndex == playerIndex);

var body = player?.ComponentBody;
var locomotion = player?.ComponentLocomotion;
if (body == null || locomotion == null) {
    return "missing";
}

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "id={0};look={1:R},{2:R};crouch={3}",
    body.Entity.Id,
    locomotion.LookAngles.X,
    locomotion.LookAngles.Y,
    body.IsCrouching);
