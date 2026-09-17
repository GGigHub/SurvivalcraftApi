var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
var inventory = player?.ComponentMiner?.Inventory;
var clothing = player?.ComponentClothing;
if (inventory == null || clothing == null) return "missing";
var widget = new Game.InventorySlotWidget();
bool moved = widget.HandleDragDrop(inventory, __FROM_SLOT__, Game.DragMode.SingleItem, clothing, __TO_SLOT__, false);
return "queued:" + moved.ToString(System.Globalization.CultureInfo.InvariantCulture);