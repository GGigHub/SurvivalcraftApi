var project = Game.GameManager.Project;
            var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
            var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
            var blockEntity = blockEntities.GetBlockEntity({0}, {1}, {2});
            var chest = blockEntity?.Entity.FindComponent<Game.ComponentChest>();
            if (chest == null) return "missing";
            bool hasTerrainCell = Game.Terrain.ExtractContents(terrain.Terrain.GetCellValue({0}, {1}, {2})) == Game.ChestBlock.Index;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "x={0};y={1};z={2};inventoryId={3};slot0Value={4};slot0Count={5};hasTerrainCell={6}",
                {0},
                {1},
                {2},
                chest.Id,
                chest.GetSlotValue(0),
                chest.GetSlotCount(0),
                hasTerrainCell);