var project = Game.GameManager.Project;
if (project == null) return "no-project";
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
if (player == null) return "no-player";
var body = player.ComponentBody;
if (body == null) return "no-body";

var pos = body.Position;
pos.Y += __RISE__;
body.Position = pos;
body.Velocity = new Vector3(0f, 0f, 0f);
body.StandingOnValue = null;

Game.NetworkManager.Queue(new Game.Network.PlayerBodyUpdatePacket(player));

string height = "no-gliding";
var gliding = player.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Elytron.ComponentGliding");
if (gliding != null) {
    var m = gliding.GetType().GetMethod("GetHeightFromGround", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    if (m != null) {
        object o = m.Invoke(gliding, null);
        height = o != null ? o.ToString() : "null";
    }
}
return "teleported:y=" + body.Position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
    + ":height=" + height;