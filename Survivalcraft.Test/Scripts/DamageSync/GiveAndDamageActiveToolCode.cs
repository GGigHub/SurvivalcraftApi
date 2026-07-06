var gameInfo = Game.GameManager.Project.FindSubsystem<Game.SubsystemGameInfo>();
            gameInfo.WorldSettings.GameMode = Game.GameMode.Survival;
            var player = Game.GameManager.Project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
            if (player == null || player.ComponentMiner?.Inventory == null) return "missing";
            var inventory = player.ComponentMiner.Inventory;
            inventory.ActiveSlotIndex = 0;
            var emptySlot = new Game.ComponentInventoryBase.Slot { Value = 0, Count = 0 };
            for (int i = 0; i < inventory.SlotsCount; i++) {
                inventory.SetSlotValue(i, emptySlot);
            }
            inventory.SetSlotValue(0, new Game.ComponentInventoryBase.Slot { Value = Game.Terrain.MakeBlockValue(Game.IronPickaxeBlock.Index), Count = 1 });
            player.ComponentMiner.DamageActiveTool(__DAMAGE_COUNT__);
            int value = inventory.GetSlotValue(0);
            int count = inventory.GetSlotCount(0);
            if (count <= 0) value = 0;
            int contents = value != 0 ? Game.Terrain.ExtractContents(value) : 0;
            int damage = value != 0 ? Game.BlocksManager.Blocks[contents].GetDamage(value) : -1;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "value={0};count={1};contents={2};damage={3}", value, count, contents, damage);
