var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
int chunkX = __CHUNK_X__;
int chunkZ = __CHUNK_Z__;
var chunk = terrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);

if (chunk == null) {
    return string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "chunkX={0};chunkZ={1};loaded=False",
        chunkX,
        chunkZ);
}

static int HashNormalizedCells(int[] values) {
    unchecked {
        int hash = (int)2166136261;
        for (int i = 0; i < values.Length; i++) {
            hash = (hash ^ Game.Terrain.ReplaceLight(values[i], 0)) * 16777619;
        }
        return hash;
    }
}

static int HashValues(int[] values) {
    unchecked {
        int hash = (int)2166136261;
        for (int i = 0; i < values.Length; i++) {
            hash = (hash ^ values[i]) * 16777619;
        }
        return hash;
    }
}

int sampleY = __SAMPLE_Y__;
int v1 = chunk.GetCellValueFast(1, sampleY, 1);
int v2 = chunk.GetCellValueFast(6, sampleY, 2);
int v3 = chunk.GetCellValueFast(10, sampleY + 1, 9);
int v4 = chunk.GetCellValueFast(14, sampleY + 2, 14);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "chunkX={0};chunkZ={1};loaded=True;threadState={2};state={3};sampleY={4};cellsHash={5};shaftsHash={6};v1={7};v2={8};v3={9};v4={10};c1={11};c2={12};c3={13};c4={14};d1={15};d2={16};d3={17};d4={18}",
    chunkX,
    chunkZ,
    chunk.ThreadState,
    chunk.State,
    sampleY,
    HashNormalizedCells(chunk.Cells),
    HashValues(chunk.Shafts),
    Game.Terrain.ReplaceLight(v1, 0),
    Game.Terrain.ReplaceLight(v2, 0),
    Game.Terrain.ReplaceLight(v3, 0),
    Game.Terrain.ReplaceLight(v4, 0),
    Game.Terrain.ExtractContents(v1),
    Game.Terrain.ExtractContents(v2),
    Game.Terrain.ExtractContents(v3),
    Game.Terrain.ExtractContents(v4),
    Game.Terrain.ExtractData(v1),
    Game.Terrain.ExtractData(v2),
    Game.Terrain.ExtractData(v3),
    Game.Terrain.ExtractData(v4));
