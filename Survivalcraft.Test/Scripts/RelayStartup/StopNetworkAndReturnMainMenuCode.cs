NetworkManager.Stop();
DialogsManager.HideAllDialogs();
if (GameManager.Project != null) {
    GameManager.SaveProject(true, true);
    GameManager.DisposeProject();
}
ScreensManager.SwitchScreen("MainMenu");
