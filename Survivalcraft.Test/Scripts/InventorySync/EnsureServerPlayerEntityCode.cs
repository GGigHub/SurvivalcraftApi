var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {0}) {
                    if (playerData.ComponentPlayer == null) {
                        playerData.SpawnPlayer(
                            players.GlobalSpawnPosition + new Engine.Vector3(4f, 0f, 4f),
                            Game.PlayerData.SpawnMode.InitialNoIntro);
                    }
                    return playerData.ComponentPlayer?.Entity.Id ?? 0;
                }
            }
            return 0;