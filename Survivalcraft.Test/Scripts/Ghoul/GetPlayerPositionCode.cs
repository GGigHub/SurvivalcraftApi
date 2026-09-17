int playerIndex = __PLAYER_INDEX__;
var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == playerIndex);
if (playerData == null || playerData.ComponentPlayer == null) {
    return "no-player";
}

var body = playerData.ComponentPlayer.ComponentBody;
return body.Position.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
    body.Position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
    body.Position.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);