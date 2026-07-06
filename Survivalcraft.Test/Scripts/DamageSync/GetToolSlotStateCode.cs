var player = Game.GameManager.Project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
            var inventory = player?.ComponentMiner?.Inventory;
            if (inventory == null) return "missing";
            int value = inventory.GetSlotValue(0);
            int count = inventory.GetSlotCount(0);
            if (count <= 0) value = 0;
            int contents = value != 0 ? Game.Terrain.ExtractContents(value) : 0;
            int damage = value != 0 ? Game.BlocksManager.Blocks[contents].GetDamage(value) : -1;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "value={0};count={1};contents={2};damage={3}", value, count, contents, damage);
