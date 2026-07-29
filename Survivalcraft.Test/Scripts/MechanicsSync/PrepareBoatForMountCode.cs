var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var mount = project.FindEntity(__MOUNT_ENTITY_ID__)?.FindComponent<Game.ComponentMount>();
if (player == null || mount == null) return false;
var riderBody = player.ComponentBody;
var mountBody = mount.ComponentBody;
mountBody.Position = riderBody.Position
    + new Engine.Vector3(1f, 0f, 0f)
    - Engine.Vector3.Transform(mount.MountOffset, mountBody.Rotation);
mountBody.Velocity = Engine.Vector3.Zero;
mountBody.IsGravityEnabled = false;
mountBody.TerrainCollidable = false;
mountBody.BodyCollidable = false;
return true;
