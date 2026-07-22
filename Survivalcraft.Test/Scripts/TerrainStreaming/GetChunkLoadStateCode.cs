var project = Game.GameManager.Project;
var terrain = project?.FindSubsystem<Game.SubsystemTerrain>();
var chunk = terrain?.Terrain.GetChunkAtCoords(__CHUNK_X__, __CHUNK_Z__);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "allocated={0};loaded={1};threadState={2};state={3}",
    chunk != null,
    chunk != null
        && chunk.ThreadState > Game.TerrainChunkState.InvalidContents4
        && (!Game.NetworkManager.IsClientRunning || chunk.IsLoaded),
    chunk?.ThreadState.ToString() ?? "null",
    chunk?.State.ToString() ?? "null");
