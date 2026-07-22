var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
int playerIndex = __PLAYER_INDEX__;

if (playerIndex < 0) {
    playerIndex = Game.NetworkManager.ServerSessions.FirstOrDefault(session => session.PlayerIndex >= 0)?.PlayerIndex ?? -1;
}

var playerData = players.PlayersData.FirstOrDefault(player => player.PlayerIndex == playerIndex);
Engine.Vector2 center = default;
int sampleY = 8;

if (playerData?.ComponentPlayer?.ComponentBody != null) {
    var position = playerData.ComponentPlayer.ComponentBody.Position;
    center = position.XZ;
    sampleY = System.Math.Clamp(Game.Terrain.ToCell(position.Y) + 3, 4, Game.TerrainChunk.Height - 4);
}
else if (terrain.TerrainUpdater.UpdateLocations.TryGetValue(playerIndex, out Game.TerrainUpdater.UpdateLocation location)) {
    center = location.Center;
}
else if (playerData != null) {
    center = playerData.SpawnPosition.XZ;
    sampleY = System.Math.Clamp(Game.Terrain.ToCell(playerData.SpawnPosition.Y) + 3, 4, Game.TerrainChunk.Height - 4);
}
else {
    center = players.GlobalSpawnPosition.XZ;
    sampleY = System.Math.Clamp(Game.Terrain.ToCell(players.GlobalSpawnPosition.Y) + 3, 4, Game.TerrainChunk.Height - 4);
}

int chunkX = Game.Terrain.ToCell(center.X) >> Game.TerrainChunk.SizeBits;
int chunkZ = Game.Terrain.ToCell(center.Y) >> Game.TerrainChunk.SizeBits;

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "chunkX={0};chunkZ={1};sampleY={2};playerIndex={3};centerX={4};centerZ={5}",
    chunkX,
    chunkZ,
    sampleY,
    playerIndex,
    center.X,
    center.Y);
