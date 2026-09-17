var project = Game.GameManager.Project;
if (project == null) return "no-project";

var pd = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData
    .FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player == null) return "no-player";

var debug = player.Entity.FindComponent<Neorxna.Components.ComponentDebugDrawing>();
if (debug == null) return "no-debug-drawing";

debug.SetHome("__HOME_NAME__");
return "set";