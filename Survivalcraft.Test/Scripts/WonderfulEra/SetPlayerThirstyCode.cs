var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
var ext = player?.Entity.FindComponent<WonderfulEra.ComponentVitalStatsExt>();
if (playerData == null) return "no-player-data";
if (player == null) return "no-player-component";
if (ext == null) {
    return "no-ext:" + string.Join(
        "|",
        player.Entity.Components.Select(component => component.GetType().FullName));
}
var field = ext.GetType().GetField(
    "m_thirsty",
    System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.NonPublic);
if (field == null) return "field-missing";
field.SetValue(ext, __THIRSTY__);
return ext.Thirsty.ToString(System.Globalization.CultureInfo.InvariantCulture);
