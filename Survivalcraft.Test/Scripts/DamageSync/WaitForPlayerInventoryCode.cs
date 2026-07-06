var player = Game.GameManager.Project
                    .FindSubsystem<Game.SubsystemPlayers>()
                    .ComponentPlayers
                    .FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
                return player?.ComponentMiner?.Inventory != null;
