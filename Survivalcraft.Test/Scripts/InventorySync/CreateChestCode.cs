var project = Game.GameManager.Project;
            var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
            var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var position = players.GlobalSpawnPosition + new Engine.Vector3(6f, 1f, 6f);
            int x = (int)System.MathF.Floor(position.X);
            int y = (int)System.MathF.Floor(position.Y);
            int z = (int)System.MathF.Floor(position.Z);

            terrain.ChangeCell(x, y, z, Game.Terrain.MakeBlockValue(Game.ChestBlock.Index));

            var blockEntity = blockEntities.GetBlockEntity(x, y, z);
            var chest = blockEntity?.Entity.FindComponent<Game.ComponentChest>();
            if (chest == null) return "missing";

            for (int i = 0; i < chest.SlotsCount; i++) {
                chest.RemoveSlotItems(i, chest.GetSlotCount(i));
            }

            int value = Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index);
            chest.AddSlotItems(0, value, 7);

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "x={0};y={1};z={2};inventoryId={3};slot0Value={4};slot0Count={5}",
                x,
                y,
                z,
                chest.Id,
                chest.GetSlotValue(0),
                chest.GetSlotCount(0));