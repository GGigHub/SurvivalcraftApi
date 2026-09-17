var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player?.ComponentMiner?.Inventory == null) return "missing";
var inventory = player.ComponentMiner.Inventory;
int value = inventory.GetSlotValue(inventory.ActiveSlotIndex);
if (value == 0) return "no-item";
var block = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(value)];
string assemblyName = block.GetType().Assembly.GetName().Name;
string typeName = block.GetType().Name;
string isSpear = MCHszr.WeaponConfigModLoader.IsSpearCategory(block).ToString();
string isSword = MCHszr.WeaponConfigModLoader.IsSwordCategory(block).ToString();
string isAxe = MCHszr.WeaponConfigModLoader.IsAxeCategory(block).ToString();
string isHammer = MCHszr.WeaponConfigModLoader.IsHammerCategory(block).ToString();
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "type={0};asm={1};spear={2};sword={3};axe={4};hammer={5}",
    typeName,
    assemblyName,
    isSpear,
    isSword,
    isAxe,
    isHammer);