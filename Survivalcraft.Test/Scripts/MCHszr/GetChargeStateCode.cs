var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer);
if (player == null) return "missing";
var input = player.ComponentInput;
var loader = ModsManager.ModLoaders.FirstOrDefault(l => l is MCHszr.UnifiedMCHszrModLoader);
if (loader == null) return "no-loader";
var t = loader.GetType();
string Get(string name) {
    var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (f == null) return "?";
    object v = f.GetValue(loader);
    return v == null ? "null" : v.ToString();
}
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "aim={0};wasAimingSpear={1};spearChargeStart={2};speedModified={3};chargingPlayer={4};isHammerCharging={5};hammerChargeStart={6};hammerChargePlayer={7}",
    input?.PlayerInput.Aim.HasValue ?? false,
    Get("m_wasAimingSpear"),
    Get("m_spearChargeStartTime"),
    Get("m_speedModified"),
    Get("m_chargingPlayer"),
    Get("m_isHammerCharging"),
    Get("m_hammerChargeStartTime"),
    Get("m_hammerChargePlayer"));
