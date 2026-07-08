var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
int chunkX = __CHUNK_X__;
int chunkZ = __CHUNK_Z__;
int sampleY = __SAMPLE_Y__;
int x = (chunkX << Game.TerrainChunk.SizeBits) + 6;
int z = (chunkZ << Game.TerrainChunk.SizeBits) + 2;
int value = Game.Terrain.MakeBlockValue(Game.GlassBlock.Index, 0, 9);

terrain.ChangeCell(x, sampleY, z, value);

int actual = terrain.Terrain.GetCellValue(x, sampleY, z);
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "chunkX={0};chunkZ={1};sampleY={2};value={3};contents={4};data={5}",
    chunkX,
    chunkZ,
    sampleY,
    Game.Terrain.ReplaceLight(actual, 0),
    Game.Terrain.ExtractContents(actual),
    Game.Terrain.ExtractData(actual));
