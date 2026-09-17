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

var sagPlayer = playerData.ComponentPlayer.Entity.FindComponent<SAGhoul.Components.ComponentSAGPlayer>();
if (sagPlayer == null) {
    return "no-sag-player-component";
}

return string.Join(";",
    "soulsValue=" + sagPlayer.SoulsValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    "spawnCd=" + sagPlayer.SpawnCd.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    "healProhibitTime=" + sagPlayer.HealProhibitTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    "inAirTimeSum=" + sagPlayer.InAirTimeSum.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    "earthquakeDamage=" + sagPlayer.EarthquakeDamage.ToString("R", System.Globalization.CultureInfo.InvariantCulture));