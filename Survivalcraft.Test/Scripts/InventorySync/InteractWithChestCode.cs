var project = Game.GameManager.Project;
            var player = project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
            if (player?.ComponentMiner == null) return false;

            var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
            int value = terrain.Terrain.GetCellValue({0}, {1}, {2});
            if (Game.Terrain.ExtractContents(value) != Game.ChestBlock.Index) return false;

            var raycast = new Game.TerrainRaycastResult {
                CellFace = new Game.CellFace({0}, {1}, {2}, 0),
                Value = value,
                Ray = new Engine.Ray3(
                    new Engine.Vector3({0} + 0.5f, {1} + 0.5f, {2} + 2f),
                    -Engine.Vector3.UnitZ),
                Distance = 1.5f
            };

            return player.ComponentMiner.Interact(raycast);