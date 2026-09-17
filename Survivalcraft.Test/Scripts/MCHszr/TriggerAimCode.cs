var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer);
if (player == null) return "missing";
var loader = ModsManager.ModLoaders.FirstOrDefault(l => l is MCHszr.UnifiedMCHszrModLoader);
if (loader == null) return "no-loader";
var method = loader.GetType().GetMethod("UpdatePlayerInputAim");
if (method == null) return "no-method";
bool playerOperated = false;
float timeInterval = 0.1f;
object[] args = new object[] { player, __AIMING__, playerOperated, timeInterval, false, false };
try {
    method.Invoke(loader, args);
} catch (System.Reflection.TargetInvocationException ex) {
    return "exception:" + (ex.InnerException?.GetType().Name ?? ex.GetType().Name);
}
bool skip = (bool)args[5];
return "skip=" + skip.ToString(System.Globalization.CultureInfo.InvariantCulture);