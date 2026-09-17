var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = pd?.ComponentPlayer;
if (player == null) return "missing";
var comps = player.Entity.FindComponents<MCHszr.ComponenthuixueHealth>();
var comp = comps.FirstOrDefault();
if (comp == null) return "no-component";
var t = comp.GetType();
string Get(string name) {
    var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (f == null) return "?";
    object v = f.GetValue(comp);
    return v == null ? "null" : v.ToString();
}
return "enhanced=" + Get("m_isEnhanced") + ";origHeal=" + Get("m_originalHealFactor");