var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
if (player?.ComponentMiner?.Inventory == null) return "missing";

var position = players.GlobalSpawnPosition + new Engine.Vector3(22f, 1f, 14f);
int x = (int)System.MathF.Floor(position.X);
int y = (int)System.MathF.Floor(position.Y);
int z = (int)System.MathF.Floor(position.Z);

var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
for (int dx = -2; dx <= 2; dx++) {
    for (int dy = 0; dy <= 4; dy++) {
        for (int dz = -2; dz <= 4; dz++) {
            terrain.ChangeCell(x + dx, y + dy, z + dz, 0);
        }
    }
}
for (int dx = -2; dx <= 2; dx++) {
    for (int dz = -2; dz <= 4; dz++) {
        terrain.ChangeCell(x + dx, y - 1, z + dz, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
    }
}

var entity = Game.DatabaseManager.CreateEntity(project, "Boat", true);
var targetBody = entity.FindComponent<Game.ComponentBody>(true);
targetBody.Position = new Engine.Vector3(x + 0.5f, y + 1f, z + 1f);
targetBody.Rotation = Engine.Quaternion.Identity;
targetBody.Velocity = Engine.Vector3.Zero;
targetBody.IsGravityEnabled = false;
targetBody.IsSmoothRiseEnabled = false;
var damage = entity.FindComponent<Game.ComponentDamage>(true);
damage.Hitpoints = 1f;
damage.m_lastHitpoints = 1f;
damage.AttackResilience = 100f;
var spawn = entity.FindComponent<Game.ComponentSpawn>();
if (spawn != null) {
    spawn.SpawnDuration = 0f;
    spawn.AutoDespawn = false;
    spawn.DespawnTime = null;
}
project.AddEntity(entity);

int activeValue = Game.Terrain.MakeBlockValue(Game.WoodenClubBlock.Index);
Game.BlocksManager.Blocks[Game.WoodenClubBlock.Index].DefaultMeleeHitProbability = 1f;
player.ComponentMiner.AttackPower = 10f;
var inventory = player.ComponentMiner.Inventory;
inventory.ActiveSlotIndex = 0;
var emptySlot = new Game.ComponentInventoryBase.Slot { Value = 0, Count = 0 };
for (int i = 0; i < inventory.SlotsCount; i++) {
    inventory.SetSlotValue(i, emptySlot);
}
inventory.SetSlotValue(0, new Game.ComponentInventoryBase.Slot { Value = activeValue, Count = 1 });

var body = player.ComponentBody;
body.Position = new Engine.Vector3(x + 0.5f, y + 1f, z + 2f);
body.Rotation = Engine.Quaternion.Identity;
body.Velocity = Engine.Vector3.Zero;
body.IsGravityEnabled = false;
body.IsSmoothRiseEnabled = false;
player.ComponentLocomotion.IsCreativeFlyEnabled = true;
player.ComponentLocomotion.LookAngles = Engine.Vector2.Zero;
player.m_lastActionTime = -1000.0;
player.Update(0f);
player.ComponentCreatureModel.Update(0f);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};expectedX={0};expectedY={1};expectedZ={2};contents=0;data=0;expectedContents=0;expectedData=0;activeValue={3};activeCount=1;entityId={4}",
    x,
    y,
    z,
    activeValue,
    entity.Id);
