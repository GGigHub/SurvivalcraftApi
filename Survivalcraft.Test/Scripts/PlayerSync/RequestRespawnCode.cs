var players = GameManager.Project.FindSubsystem<SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.IsMainPlayer);
if (playerData == null) {
    return "missing";
}

playerData.readyToRestart = true;
return string.Create(
    System.Globalization.CultureInfo.InvariantCulture,
    $"index={playerData.PlayerIndex};state={playerData.m_stateMachine.CurrentState ?? string.Empty};readyToRestart={playerData.readyToRestart}");
