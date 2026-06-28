GameManager.IsProjectAutoUpdateEnabled = false;

Action waitForGameReady = null;
waitForGameReady = () => {
    if (GameManager.Project != null
        && ScreensManager.CurrentScreen != null
        && ScreensManager.CurrentScreen.GetType().Name == ""GameScreen""
        && !ScreensManager.IsAnimating) {
        Log.Information(""WorldReproducibility fixed step started."");
        GameManager.UpdateProjectFixed(1200, 1f / 60f);
        Log.Information(""WorldReproducibility fixed step completed."");
        GameManager.IsProjectAutoUpdateEnabled = true;
        NetworkManager.Stop();
        GameManager.SaveProject(true, true);
        GameManager.DisposeProject();
        ScreensManager.SwitchScreen(""MainMenu"");
    } else {
        Engine.Time.QueueFrameIndexDelayedExecution(Engine.Time.FrameIndex + 1, waitForGameReady);
    }
};
waitForGameReady();

WorldsManager.UpdateWorldsList();
ScreensManager.SwitchScreen(""GameLoading"", WorldsManager.WorldInfos[0], null);
