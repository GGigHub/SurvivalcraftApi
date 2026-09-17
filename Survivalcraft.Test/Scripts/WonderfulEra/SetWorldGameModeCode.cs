var gameInfo = Game.GameManager.Project.FindSubsystem<Game.SubsystemGameInfo>();
            gameInfo.WorldSettings.GameMode = Game.GameMode.Survival;
            return gameInfo.WorldSettings.GameMode.ToString();