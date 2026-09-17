var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
int x = __X__;
int y = __Y__;
int z = __Z__;
for (int dx = -3; dx <= 3; dx++) {
    for (int dy = 0; dy <= 6; dy++) {
        for (int dz = -3; dz <= 10; dz++) {
            terrain.ChangeCell(x + dx, y + dy, z + dz, 0);
        }
    }
}
for (int dx = -3; dx <= 3; dx++) {
    for (int dz = -3; dz <= 10; dz++) {
        terrain.ChangeCell(x + dx, y - 1, z + dz, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
    }
}
var entity = Game.DatabaseManager.CreateEntity(project, "Boat", true);
var body = entity.FindComponent<Game.ComponentBody>(true);
body.Position = new Engine.Vector3(__DUMMY_X__f, __DUMMY_Y__f, __DUMMY_Z__f);
body.Rotation = Engine.Quaternion.Identity;
body.Velocity = Engine.Vector3.Zero;
body.IsGravityEnabled = false;
body.IsSmoothRiseEnabled = false;
var damage = entity.FindComponent<Game.ComponentDamage>(true);
damage.Hitpoints = __HEALTH__f;
damage.m_lastHitpoints = __HEALTH__f;
damage.AttackResilience = 1f;
var componentSpawn = entity.FindComponent<Game.ComponentSpawn>();
if (componentSpawn != null) {
    componentSpawn.SpawnDuration = 0f;
    componentSpawn.AutoDespawn = false;
    componentSpawn.DespawnTime = null;
}
project.AddEntity(entity);
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", entity.Id);
