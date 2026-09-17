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

sagPlayer.SoulsValue = __SOULS_VALUE__f;
return "ok";