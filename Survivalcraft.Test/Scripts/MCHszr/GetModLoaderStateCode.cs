var loader = ModsManager.ModLoaders.FirstOrDefault(l => l is MCHszr.UnifiedMCHszrModLoader);
if (loader == null) return "no-loader";
var t = loader.GetType();
string Get(string name) {
    var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (f == null) return "?";
    object v = f.GetValue(loader);
    return v == null ? "null" : v.ToString();
}
bool HasPlayer(string name) {
    var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    return f != null && f.GetValue(loader) != null;
}
return string.Join(";",
    "dashPlayer=" + HasPlayer("m_dashingPlayer"),
    "dashSpeed=" + Get("m_dashSpeed"),
    "dashDuration=" + Get("m_dashDuration"),
    "isSpearDash=" + Get("m_isSpearDash"),
    "isCriticalHit=" + Get("m_isCriticalHit"),
    "isSweepAttack=" + Get("m_isSweepAttack"),
    "isHammerCharging=" + Get("m_isHammerCharging"),
    "isHammerFall=" + Get("m_isHammerFall"),
    "isHammerShockwave=" + Get("m_isHammerShockwave"),
    "chargeTime=" + Get("m_chargeTime"),
    "speedModified=" + Get("m_speedModified"));
