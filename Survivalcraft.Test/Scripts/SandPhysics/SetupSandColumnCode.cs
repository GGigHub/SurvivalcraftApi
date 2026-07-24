var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer) ?? players.ComponentPlayers.FirstOrDefault();
var basePosition = player?.ComponentBody.Position ?? players.GlobalSpawnPosition;

int chunkX = Game.Terrain.ToCell(basePosition.X) >> Game.TerrainChunk.SizeBits;
int chunkZ = Game.Terrain.ToCell(basePosition.Z) >> Game.TerrainChunk.SizeBits;
int originX = (chunkX << Game.TerrainChunk.SizeBits) + 8;
int originZ = (chunkZ << Game.TerrainChunk.SizeBits) + 8;
int sampleY = System.Math.Clamp(Game.Terrain.ToCell(basePosition.Y) + 2, 10, Game.TerrainChunk.Height - 10);

var center = new Engine.Vector2(originX + 0.5f, originZ + 0.5f);
terrain.TerrainUpdater.SetUpdateLocation(9001, center, 0f, 24f);
terrain.TerrainUpdater.UpdateFullySynchronously();

int graniteValue = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index, 0, 0);
int sandValue = Game.Terrain.MakeBlockValue(Game.SandBlock.Index, 0, 0);

// Base floor below support
terrain.ChangeCell(originX, sampleY - 2, originZ, graniteValue);
// Support block
terrain.ChangeCell(originX, sampleY - 1, originZ, graniteValue);
// Sand block
terrain.ChangeCell(originX, sampleY, originZ, sandValue);
terrain.TerrainUpdater.UpdateFullySynchronously();

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};supportValue={3};sandValue={4}",
    originX,
    sampleY,
    originZ,
    graniteValue,
    sandValue);
