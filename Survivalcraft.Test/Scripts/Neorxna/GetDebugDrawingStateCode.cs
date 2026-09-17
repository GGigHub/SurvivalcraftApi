var project = Game.GameManager.Project;
if (project == null) return "no-project";

var pd = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData
    .FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player == null) return "no-player";

var debug = player.Entity.FindComponent<Neorxna.Components.ComponentDebugDrawing>();
if (debug == null) return "no-debug-drawing";

var back = debug.BackPosition;
var homes = string.Join(",", debug.ExportAllHomes().OrderBy(h => h.Key)
    .Select(h => h.Key + "@" + string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", h.Value.X, h.Value.Y, h.Value.Z)));

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "back={0};homes={1}",
    back.HasValue
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", back.Value.X, back.Value.Y, back.Value.Z)
        : "none",
    homes);