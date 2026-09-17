var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player?.ComponentMiner?.Inventory == null) return "missing";
var behavior = project.FindSubsystem<MCHszr.SubsystemSpearEditBehavior>();
if (behavior == null) return "no-behavior";
var inventory = player.ComponentMiner.Inventory;
int slotIndex = inventory.ActiveSlotIndex;
if (player.DragHostWidget == null) return "no-draghost";
bool toggled;
try {
    toggled = behavior.OnEditInventoryItem(inventory, slotIndex, player);
} catch (System.Exception ex) {
    return "exception:" + ex.GetType().Name;
}
int value = inventory.GetSlotValue(slotIndex);
var block = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(value)];
int pose = behavior.GetPlayerPoseState(player.PlayerData.Name, block);
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "toggled={0};pose={1}", toggled, pose);
