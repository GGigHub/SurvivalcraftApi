var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {0} && playerData.ComponentPlayer != null) {
                    var health = playerData.ComponentPlayer.ComponentHealth;
                    health.IsInvulnerable = false;
                    health.m_regenerateLifeEnabled = false;
                    health.Health = {1}f;
                    health.m_lastHealth = {1}f;
                    health.CauseOfDeath = null;
                    health.DeathTime = null;
                    return health.Health;
                }
            }
            return -1f;