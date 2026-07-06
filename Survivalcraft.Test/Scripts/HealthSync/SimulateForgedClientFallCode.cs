var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var p in players.PlayersData) {
                if (p.PlayerIndex == {0} && p.ComponentPlayer != null) {
                    p.ComponentPlayer.ComponentHealth.Injure(0.1f, null, false, "Fall");
                }
            }
            return true;