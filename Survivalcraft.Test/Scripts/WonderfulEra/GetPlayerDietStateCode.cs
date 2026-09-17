var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
foreach (var playerData in players.PlayersData) {
    if (playerData.PlayerIndex == __PLAYER_INDEX__ && playerData.ComponentPlayer != null) {
        var player = playerData.ComponentPlayer;
        var stats = player.ComponentVitalStats;
        if (stats == null) return "missing";
        var ext = player.Entity.FindComponent<WonderfulEra.ComponentVitalStatsExt>();
        var sickness = player.ComponentSickness;
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6},{7:R}",
            stats.Food,
            stats.Stamina,
            stats.Sleep,
            stats.Wetness,
            stats.Temperature,
            ext?.Thirsty ?? -1f,
            sickness?.IsSick == true ? 1 : 0,
            sickness?.m_sicknessDuration ?? 0f);
    }
}
return "";