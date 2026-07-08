var screen = ScreensManager.CurrentScreen as PlayerScreen;
if (screen == null) {
    return "not-player-screen";
}
if (screen.m_playerData == null || screen.m_nameTextBox == null) {
    return "player-screen-not-ready";
}

screen.m_playerData.Name = "__PLAYER_NAME__";
screen.m_nameTextBox.Text = "__PLAYER_NAME__";
return string.Create(
    System.Globalization.CultureInfo.InvariantCulture,
    $"index={screen.m_playerData.PlayerIndex};name={screen.m_playerData.Name};skin={screen.m_playerData.CharacterSkinName};class={screen.m_playerData.PlayerClass}");
