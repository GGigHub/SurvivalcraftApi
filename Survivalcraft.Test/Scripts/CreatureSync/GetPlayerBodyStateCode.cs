int playerIndex = __PLAYER_INDEX__;
var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == playerIndex);
var body = player?.ComponentBody;
if (body == null) {
    return "missing";
}

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

