var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {0} && playerData.ComponentPlayer != null) {
                    return playerData.ComponentPlayer.ComponentHealth.Health;
                }
            }
            return -1f;