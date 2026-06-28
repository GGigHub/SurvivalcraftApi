GameManager.IsProjectAutoUpdateEnabled = false;

Action waitForGameReady = null;
int lastChunksCount = -1;
int unchangedFrames = 0;

waitForGameReady = () => {
    var terrainSubsystem = GameManager.Project?.FindSubsystem<SubsystemTerrain>(true);
    var playerSubsystem = GameManager.Project?.FindSubsystem<SubsystemPlayers>(true);
    var skySubsystem = GameManager.Project?.FindSubsystem<SubsystemSky>(true);
    if (terrainSubsystem != null && playerSubsystem != null && skySubsystem != null && playerSubsystem.PlayersData.Count > 0 && !ScreensManager.IsAnimating) {
        float vis = skySubsystem.VisibilityRange;
        int playerIndex = playerSubsystem.PlayersData[0].PlayerIndex;

        // Manually set update location so chunks can load without updating the game loop
        terrainSubsystem.TerrainUpdater.SetUpdateLocation(
            playerIndex,
            playerSubsystem.PlayersData[0].SpawnPosition.XZ,
            vis, 64f);

        // Manually pump TerrainUpdater without advancing game time
        terrainSubsystem.TerrainUpdater.Update();

        bool allValid = terrainSubsystem.Terrain.AllocatedChunks.Length > 0;
        foreach (var chunk in terrainSubsystem.Terrain.AllocatedChunks) {
            if (chunk.State < TerrainChunkState.Valid) {
                allValid = false;
                break;
            }
        }

        if (allValid) {
            unchangedFrames++;
        }
        else {
            unchangedFrames = 0;
        }

        if (unchangedFrames > 60) {
            foreach (var pd in playerSubsystem.PlayersData) {
                pd.m_stateMachine.TransitionTo("Playing");
            }
            Log.Information("WorldReproducibility fixed step started. Chunks: " + terrainSubsystem.Terrain.AllocatedChunks.Length);
            GameManager.UpdateProjectFixed(1200, 1f / 60f);
            Log.Information("WorldReproducibility fixed step completed.");
            GameManager.IsProjectAutoUpdateEnabled = true;
            NetworkManager.Stop();
            GameManager.SaveProject(true, true);
            GameManager.DisposeProject();
            ScreensManager.SwitchScreen("MainMenu");
            return;
        }
    }
    Engine.Time.QueueFrameIndexDelayedExecution(Engine.Time.FrameIndex + 1, waitForGameReady);
};

WorldsManager.UpdateWorldsList();
ScreensManager.SwitchScreen("GameLoading", WorldsManager.WorldInfos[0], null);
GameEntitySystem.Project.OnProjectLoad += _ => waitForGameReady();
