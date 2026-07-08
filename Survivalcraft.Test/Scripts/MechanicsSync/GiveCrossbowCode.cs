var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var inventory = player?.ComponentMiner?.Inventory;
if (inventory == null) return "missing";
inventory.ActiveSlotIndex = 0;
for (int i = 0; i < inventory.SlotsCount; i++) {
    inventory.SetSlotValue(i, new Game.ComponentInventoryBase.Slot { Value = 0, Count = 0 });
}
int value = Game.Terrain.MakeBlockValue(Game.CrossbowBlock.Index, 0, Game.CrossbowBlock.SetDraw(0, 0));
inventory.SetSlotValue(0, new Game.ComponentInventoryBase.Slot { Value = value, Count = 1 });
int data = Game.Terrain.ExtractData(inventory.GetSlotValue(0));
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "value={0};draw={1}", inventory.GetSlotValue(0), Game.CrossbowBlock.GetDraw(data));
