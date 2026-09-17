var project = Game.GameManager.Project;
if (project == null) return "no-project";
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
if (player == null) return "no-player";
var clothing = player.ComponentClothing;
if (clothing == null) return "no-clothing";

int blockIndex = Game.BlocksManager.GetBlockIndex("ElytraClothingBlock", false);
if (blockIndex < 0) return "no-elytra-block";
int blockValue = Game.Terrain.MakeBlockValue(blockIndex, 0, 0);

var torso = new System.Collections.Generic.List<int>();
foreach (int item in clothing.GetClothes(Game.ClothingSlot.Torso)) {
    if (item != 0 && !torso.Contains(item)) torso.Add(item);
}
if (!torso.Contains(blockValue)) {
    torso.Add(blockValue);
}

clothing.SetClothes(Game.ClothingSlot.Torso, torso);

var joined = string.Join(",", clothing.GetClothes(Game.ClothingSlot.Torso));
return "ok:blockIndex=" + blockIndex + ":blockValue=" + blockValue + ":torso=" + joined;