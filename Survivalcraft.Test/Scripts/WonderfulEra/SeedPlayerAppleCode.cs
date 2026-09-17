var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var inventory = playerData?.ComponentPlayer?.ComponentMiner?.Inventory;
if (inventory == null) return "missing";
inventory.RemoveSlotItems(0, inventory.GetSlotCount(0));
int apple = Game.BlocksManager.GetBlockIndex<WonderfulEra.AppleBlock>(false, false);
inventory.AddSlotItems(0, Game.Terrain.MakeBlockValue(apple), 8);
Game.Network.InventoryPacketHelpers.QueueInventorySync(inventory);
return "seeded";