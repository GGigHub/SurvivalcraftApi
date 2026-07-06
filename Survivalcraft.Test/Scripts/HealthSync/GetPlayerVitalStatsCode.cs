var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {0} && playerData.ComponentPlayer != null) {
                    var stats = playerData.ComponentPlayer.ComponentVitalStats;
                    if (stats != null) {
                        return stats.Food.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Stamina.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Sleep.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Wetness.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                               stats.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
            return "";