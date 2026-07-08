var players = GameManager.Project.FindSubsystem<SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
if (playerData?.ComponentPlayer == null) {
    return "missing";
}

playerData.ComponentPlayer.ComponentLevel.AddExperience(__COUNT__, false);
return string.Create(
    System.Globalization.CultureInfo.InvariantCulture,
    $"index={playerData.PlayerIndex};level={playerData.Level:R}");
