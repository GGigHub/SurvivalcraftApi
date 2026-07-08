var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
if (player?.ComponentMiner?.Inventory == null) return "missing";

var position = players.GlobalSpawnPosition + new Engine.Vector3(12f, 1f, 14f);
int x = (int)System.MathF.Floor(position.X);
int y = (int)System.MathF.Floor(position.Y);
int z = (int)System.MathF.Floor(position.Z);

terrain.ChangeCell(x, y, z, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
terrain.ChangeCell(x, y, z + 1, 0);

var inventory = player.ComponentMiner.Inventory;
inventory.ActiveSlotIndex = 0;
var emptySlot = new Game.ComponentInventoryBase.Slot { Value = 0, Count = 0 };
for (int i = 0; i < inventory.SlotsCount; i++) {
    inventory.SetSlotValue(i, emptySlot);
}
inventory.SetSlotValue(0, new Game.ComponentInventoryBase.Slot {
    Value = Game.Terrain.MakeBlockValue(Game.PlanksBlock.Index),
    Count = 1
});

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

int expectedValue = Game.Terrain.MakeBlockValue(Game.PlanksBlock.Index);
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};expectedX={0};expectedY={1};expectedZ={3};contents={4};data={5};expectedContents={6};expectedData={7}",
    x,
    y,
    z,
    z + 1,
    Game.Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z + 1)),
    Game.Terrain.ExtractData(terrain.Terrain.GetCellValue(x, y, z + 1)),
    Game.Terrain.ExtractContents(expectedValue),
    Game.Terrain.ExtractData(expectedValue));
