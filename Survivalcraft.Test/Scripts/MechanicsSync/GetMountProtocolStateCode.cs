var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var rider = player?.ComponentRider;
var mount = project.FindEntity(__MOUNT_ENTITY_ID__)?.FindComponent<Game.ComponentMount>();
if (rider == null || mount == null) return "missing";
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "riderLifecycle={0};mountLifecycle={1};mountRevision={2}",
    Game.Network.EntityLifecycleTracker.GetAppliedRevision(project, rider.Entity.Id),
    Game.Network.EntityLifecycleTracker.GetAppliedRevision(project, mount.Entity.Id),
    Game.Network.NetworkStateRevisionTracker.GetAppliedRevision(
        project,
        Game.Network.NetworkStateRevisionDomain.Mount,
        rider.Entity.Id));
