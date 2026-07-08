var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var basePosition = players.GlobalSpawnPosition;
if (basePosition == Engine.Vector3.Zero) basePosition = new Engine.Vector3(0f, 80f, 0f);
var entity = Game.DatabaseManager.CreateEntity(project, "Boat", true);
var body = entity.FindComponent<Game.ComponentBody>(true);
body.Position = basePosition + new Engine.Vector3(__X_OFFSET__f, __Y_OFFSET__f, __Z_OFFSET__f);
body.Rotation = Engine.Quaternion.Identity;
body.IsGravityEnabled = false;
var damage = entity.FindComponent<Game.ComponentDamage>(true);
damage.Hitpoints = 1f;
damage.m_lastHitpoints = 1f;
var spawn = entity.FindComponent<Game.ComponentSpawn>();
if (spawn != null) {
    spawn.SpawnDuration = 0f;
    spawn.AutoDespawn = false;
    spawn.DespawnTime = null;
}
project.AddEntity(entity);
return entity.Id;
