var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var inventory = player?.ComponentMiner?.Inventory;
if (inventory == null) return "missing";
int value = inventory.GetSlotValue(0);
int draw = Game.Terrain.ExtractContents(value) == Game.CrossbowBlock.Index ? Game.CrossbowBlock.GetDraw(Game.Terrain.ExtractData(value)) : -1;
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "value={0};draw={1}", value, draw);
