var project = Game.GameManager.Project;
if (project == null) return "no-project";

var elytronMod = ModsManager.ModList.FirstOrDefault(m => m.modInfo != null && m.modInfo.PackageName == "Elytron");
var compatNetMod = ModsManager.ModList.FirstOrDefault(m => m.modInfo != null && m.modInfo.PackageName == "compatnet");
var assembly = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Elytron");

string glidingType = "missing-type";
if (assembly != null) {
    var t = assembly.GetType("Elytron.ComponentGliding");
    if (t != null) glidingType = t.FullName;
}

int blockIndex = Game.BlocksManager.GetBlockIndex("ElytraClothingBlock", false);
string blockType = blockIndex >= 0 ? Game.BlocksManager.Blocks[blockIndex].GetType().FullName : "not-found";

var playerData = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
string glidingOnPlayer = "no-player";
if (player != null) {
    var gliding = player.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Elytron.ComponentGliding");
    var accessory = player.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Elytron.ComponentElytraAccessory");
    glidingOnPlayer = (gliding != null ? "gliding=true" : "gliding=false") + ";" + (accessory != null ? "accessory=true" : "accessory=false");
}

return string.Join(";",
    "project=true",
    "elytron=" + (elytronMod != null),
    "compatnet=" + (compatNetMod != null),
    "gliding=" + glidingType,
    "blockIndex=" + blockIndex,
    "blockType=" + blockType,
    "components=" + glidingOnPlayer);