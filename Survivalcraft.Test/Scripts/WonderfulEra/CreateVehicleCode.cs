int playerIndex = __PLAYER_INDEX__;
var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == playerIndex);
var basePosition = player?.ComponentBody.Position ?? players.GlobalSpawnPosition;
if (basePosition == Engine.Vector3.Zero) basePosition = new Engine.Vector3(0f, 80f, 0f);

var entity = Game.DatabaseManager.CreateEntity(project, "AE86_Car", true);
var body = entity.FindComponent<Game.ComponentBody>(true);
body.Position = basePosition + new Engine.Vector3(__OFFSET_X__f, __OFFSET_Y__f, __OFFSET_Z__f);
body.Rotation = Engine.Quaternion.Identity;
body.Velocity = Engine.Vector3.Zero;
body.IsGravityEnabled = __GRAVITY__;
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