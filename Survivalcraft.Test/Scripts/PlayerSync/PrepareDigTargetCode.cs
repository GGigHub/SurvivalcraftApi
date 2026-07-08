var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
if (player?.ComponentMiner?.Inventory == null) return "missing";

var position = players.GlobalSpawnPosition + new Engine.Vector3(16f, 1f, 14f);
int x = (int)System.MathF.Floor(position.X);
int y = (int)System.MathF.Floor(position.Y);
int z = (int)System.MathF.Floor(position.Z);

terrain.ChangeCell(x, y, z, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index));
terrain.ChangeCell(x, y + 1, z, 0);

int toolValue = Game.Terrain.MakeBlockValue(Game.DiamondShovelBlock.Index);
var inventory = player.ComponentMiner.Inventory;
inventory.ActiveSlotIndex = 0;
var emptySlot = new Game.ComponentInventoryBase.Slot { Value = 0, Count = 0 };
for (int i = 0; i < inventory.SlotsCount; i++) {
    inventory.SetSlotValue(i, emptySlot);
}
inventory.SetSlotValue(0, new Game.ComponentInventoryBase.Slot { Value = toolValue, Count = 1 });

var body = player.ComponentBody;
body.Position = new Engine.Vector3(x + 0.5f, y + 1f, z + 3f);
body.Rotation = Engine.Quaternion.Identity;
body.Velocity = Engine.Vector3.Zero;
body.IsGravityEnabled = false;
body.IsSmoothRiseEnabled = false;
player.ComponentLocomotion.IsCreativeFlyEnabled = true;
player.m_lastActionTime = -1000.0;
player.Update(0f);
player.ComponentCreatureModel.Update(0f);

int value = terrain.Terrain.GetCellValue(x, y, z);
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};expectedX={0};expectedY={1};expectedZ={2};contents={3};data={4};expectedContents=0;expectedData=0;activeValue={5};activeCount=1",
    x,
    y,
    z,
    Game.Terrain.ExtractContents(value),
    Game.Terrain.ExtractData(value),
    toolValue);
