var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
if (player == null) return "no-player";

var basePosition = player.ComponentBody.Position;
int chunkX = Game.Terrain.ToCell(basePosition.X) >> Game.TerrainChunk.SizeBits;
int chunkZ = Game.Terrain.ToCell(basePosition.Z) >> Game.TerrainChunk.SizeBits;
int originX = (chunkX << Game.TerrainChunk.SizeBits) + 8;
int originZ = (chunkZ << Game.TerrainChunk.SizeBits) + 8;
int sampleY = System.Math.Clamp(Game.Terrain.ToCell(basePosition.Y) + 2, 10, Game.TerrainChunk.Height - 10);

var center = new Engine.Vector2(originX + 0.5f, originZ + 0.5f);
terrain.TerrainUpdater.SetUpdateLocation(9001, center, 0f, 24f);
terrain.TerrainUpdater.UpdateFullySynchronously();

int graniteValue = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index);
int sandValue = Game.Terrain.MakeBlockValue(Game.SandBlock.Index);

// Clear a pocket around the column so the injected dig ray and the fall are unobstructed.
for (int dx = -2; dx <= 2; dx++) {
    for (int dz = -2; dz <= 2; dz++) {
        for (int dy = sampleY - 1; dy <= sampleY + 2; dy++) {
            terrain.ChangeCell(originX + dx, dy, originZ + dz, 0);
        }
    }
}

// Floor below support
terrain.ChangeCell(originX, sampleY - 2, originZ, graniteValue);
// Bottom sand (dig target)
terrain.ChangeCell(originX, sampleY - 1, originZ, sandValue);
// Top sand (falls after the bottom sand is dug)
terrain.ChangeCell(originX, sampleY, originZ, sandValue);
terrain.TerrainUpdater.UpdateFullySynchronously();

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};supportValue={3};sandValue={4}",
    originX,
    sampleY,
    originZ,
    sandValue,
    sandValue);
