var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player == null) return "missing";
var center = player.ComponentBody.Position;
var subsystem = project.FindSubsystem<Game.SubsystemProjectiles>();
var list = subsystem.Projectiles
    .Where(p => p != null && Engine.Vector3.Distance(p.Position, center) <= __RADIUS__)
    .Select(p => string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "{0}:{1:R}:{2:R}:{3:R}",
        p.Value,
        p.Position.X,
        p.Position.Y,
        p.Position.Z))
    .OrderBy(s => s, System.StringComparer.Ordinal);
return string.Join(";", list);
