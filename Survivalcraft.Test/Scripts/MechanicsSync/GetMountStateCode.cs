var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var rider = player?.ComponentRider;
if (rider == null) return "missing";
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "mount={0};animating={1};dismounting={2}",
    rider.Mount?.Entity.Id ?? 0,
    rider.m_isAnimating ? 1 : 0,
    rider.m_isDismounting ? 1 : 0);
