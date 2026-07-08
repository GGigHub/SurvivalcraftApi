var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer) ?? players.ComponentPlayers.FirstOrDefault();
var basePosition = player?.ComponentBody.Position ?? players.GlobalSpawnPosition;

int chunkX = Game.Terrain.ToCell(basePosition.X) >> Game.TerrainChunk.SizeBits;
int chunkZ = Game.Terrain.ToCell(basePosition.Z) >> Game.TerrainChunk.SizeBits;
int sampleY = System.Math.Clamp(Game.Terrain.ToCell(basePosition.Y) + 3, 4, Game.TerrainChunk.Height - 4);
int originX = chunkX << Game.TerrainChunk.SizeBits;
int originZ = chunkZ << Game.TerrainChunk.SizeBits;
var center = new Engine.Vector2(originX + 8f, originZ + 8f);

terrain.TerrainUpdater.SetUpdateLocation(9000, center, 0f, 24f);
terrain.TerrainUpdater.UpdateFullySynchronously();

terrain.ChangeCell(originX + 1, sampleY, originZ + 1, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index, 0, 2));
terrain.ChangeCell(originX + 6, sampleY, originZ + 2, Game.Terrain.MakeBlockValue(Game.BasaltBlock.Index, 0, 5));
terrain.ChangeCell(originX + 10, sampleY + 1, originZ + 9, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index, 0, 0));
terrain.ChangeCell(originX + 14, sampleY + 2, originZ + 14, Game.Terrain.MakeBlockValue(Game.SandBlock.Index, 0, 0));
terrain.TerrainUpdater.UpdateFullySynchronously();

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "chunkX={0};chunkZ={1};sampleY={2};x1={3};z1={4}",
    chunkX,
    chunkZ,
    sampleY,
    originX + 1,
    originZ + 1);
