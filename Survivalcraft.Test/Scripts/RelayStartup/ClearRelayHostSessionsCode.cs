var relay = Game.NetworkManager.RelayHost;
if (relay == null) {
    return false;
}
var field = typeof(Game.Network.RelayHost).GetField(
    "_sessions",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
field ??= typeof(Game.Network.RelayHost).GetField(
    "m_sessions",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
if (field == null) {
    return false;
}
((System.Collections.IDictionary)field.GetValue(relay)).Clear();
return true;
