var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {0} && playerData.ComponentPlayer != null) {
                    var stats = playerData.ComponentPlayer.ComponentVitalStats;
                    if (stats != null) {
                        stats.Food = {1}f;
                        stats.Stamina = {2}f;
                        stats.Sleep = {3}f;
                        stats.Wetness = {4}f;
                        stats.Temperature = {5}f;
                    }
                    return true;
                }
            }
            return false;