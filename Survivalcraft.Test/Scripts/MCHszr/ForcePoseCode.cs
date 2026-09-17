var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player?.ComponentMiner?.Inventory == null) return "missing";
var inventory = player.ComponentMiner.Inventory;
int value = inventory.GetSlotValue(inventory.ActiveSlotIndex);
if (value == 0) return "no-item";
var block = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(value)];
var behavior = project.FindSubsystem<MCHszr.SubsystemSpearEditBehavior>();
if (behavior == null) return "no-behavior";
var dictField = behavior.GetType().GetField("m_playerPoseStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
if (dictField == null) return "no-field";
var dict = (System.Collections.Generic.Dictionary<string, int>)dictField.GetValue(behavior);
string key = player.PlayerData.Name + "_" + behavior.GetWeaponGroupFor(block);
if (__TARGET_POSE__ == 0) {
    dict.Remove(key);
} else {
    dict[key] = __TARGET_POSE__;
}
return behavior.GetPlayerPoseState(player.PlayerData.Name, block).ToString(System.Globalization.CultureInfo.InvariantCulture);
