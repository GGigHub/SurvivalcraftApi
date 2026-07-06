var playerIndex = Game.NetworkManager.PlayerIndex;
            var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == playerIndex && playerData.ComponentPlayer != null) {
                    return playerData.ComponentPlayer.ComponentGui.ModalPanelWidget is Game.ClothingWidget;
                }
            }
            return false;