var project = Game.GameManager.Project;
if (project == null) return "no-project";
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
if (player == null) return "no-player";
var body = player.ComponentBody;
if (body == null) return "no-body";
var gliding = player.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Elytron.ComponentGliding");
if (gliding == null) return "no-gliding";

var t = gliding.GetType();
object activeObj = t.GetProperty("IsGlidingActive").GetValue(gliding);
object poseObj = t.GetField("m_glidePoseFactor").GetValue(gliding);
object bankObj = t.GetProperty("CurrentBankRoll").GetValue(gliding);
object barrelObj = t.GetProperty("IsBarrelRollActive").GetValue(gliding);
object barrelProgressObj = t.GetField("m_barrelRollProgress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(gliding);

string cameraName = "none";
try {
    var gw = player.GameWidget;
    if (gw != null && gw.ActiveCamera != null) cameraName = gw.ActiveCamera.GetType().Name;
} catch { }

var inv = System.Globalization.CultureInfo.InvariantCulture;
return string.Format(inv,
    "active={0};pose={1:R};bank={2:R};barrel={3};barrelProgress={4:R};vx={5:R};vy={6:R};vz={7:R};px={8:R};py={9:R};pz={10:R};standing={11};camera={12}",
    activeObj,
    poseObj,
    bankObj,
    barrelObj,
    barrelProgressObj,
    body.Velocity.X,
    body.Velocity.Y,
    body.Velocity.Z,
    body.Position.X,
    body.Position.Y,
    body.Position.Z,
    body.StandingOnValue.HasValue,
    cameraName);