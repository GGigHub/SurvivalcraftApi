var gameInfo = Game.GameManager.Project.FindSubsystem<Game.SubsystemGameInfo>();
gameInfo.WorldSettings.GameMode = Game.GameMode.Survival;
var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var inventory = pd?.ComponentPlayer?.ComponentMiner?.Inventory;
if (inventory == null) return "missing";
inventory.ActiveSlotIndex = 0;
var emptySlot = new Game.ComponentInventoryBase.Slot { Value = 0, Count = 0 };
for (int i = 0; i < inventory.SlotsCount; i++) {
    inventory.SetSlotValue(i, emptySlot);
}
inventory.SetSlotValue(0, new Game.ComponentInventoryBase.Slot { Value = __ITEM_VALUE__, Count = __ITEM_COUNT__ });
inventory.ActiveSlotIndex = 0;
var block = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(__ITEM_VALUE__)];
if (block != null) {
    block.DefaultMeleeHitProbability = 1f;
}
if (pd?.ComponentPlayer != null) {
    pd.ComponentPlayer.PlayerData.Level = 10f;
    pd.ComponentPlayer.ComponentLevel.AddExperience(300, false);
}
return "seeded";
