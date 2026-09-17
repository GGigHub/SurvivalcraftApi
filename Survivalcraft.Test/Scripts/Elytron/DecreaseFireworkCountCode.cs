var project = Game.GameManager.Project;
if (project == null) return "no-project";
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
if (player == null) return "no-player";
var inventory = player.ComponentMiner?.Inventory;
if (inventory == null) return "no-inventory";

int slot = inventory.ActiveSlotIndex;
int removed = inventory.RemoveSlotItems(slot, __COUNT__);

return "ok:removed=" + removed
    + ":slotValue=" + inventory.GetSlotValue(slot)
    + ":slotCount=" + inventory.GetSlotCount(slot);