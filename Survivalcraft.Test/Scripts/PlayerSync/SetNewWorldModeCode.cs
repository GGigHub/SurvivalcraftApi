if (ScreensManager.CurrentScreen is not NewWorldScreen screen || screen.m_worldSettings == null) {
    return "not-new-world";
}

screen.m_worldSettings.GameMode = GameMode.__GAME_MODE__;
return screen.m_worldSettings.GameMode.ToString();
