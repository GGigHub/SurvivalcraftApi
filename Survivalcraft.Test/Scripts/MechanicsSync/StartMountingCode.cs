var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var rider = player?.ComponentRider;
var mount = project.FindEntity(__MOUNT_ENTITY_ID__)?.FindComponent<Game.ComponentMount>();
if (rider == null || mount == null) return "missing";
rider.StartMounting(mount);
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "mount={0};animating={1};dismounting={2}",
    rider.Mount?.Entity.Id ?? 0,
    rider.m_isAnimating ? 1 : 0,
    rider.m_isDismounting ? 1 : 0);
