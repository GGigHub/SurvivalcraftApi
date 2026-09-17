var project = Game.GameManager.Project;
var player = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData.FirstOrDefault(pd => pd.PlayerIndex == __PLAYER_INDEX__)?.ComponentPlayer;
if (player == null) return "missing";
return player.Entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);