var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();

int chunkX = __CHUNK_X__;
int chunkZ = __CHUNK_Z__;

var center = new Engine.Vector2(
    (chunkX << Game.TerrainChunk.SizeBits) + 8f,
    (chunkZ << Game.TerrainChunk.SizeBits) + 8f);

int playerIndex = Game.NetworkManager.PlayerIndex;
terrain.TerrainUpdater.SetUpdateLocation(playerIndex, center, 0f, 24f);
terrain.TerrainUpdater.Update();

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "chunkX={0};chunkZ={1};requested=True",
    chunkX,
    chunkZ);