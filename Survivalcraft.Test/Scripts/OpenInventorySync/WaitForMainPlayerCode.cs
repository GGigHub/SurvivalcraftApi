return Game.GameManager.Project
                    .FindSubsystem<Game.SubsystemPlayers>()
                    .ComponentPlayers
                    .Any(p => p.PlayerData.IsMainPlayer && p.ComponentMiner != null);