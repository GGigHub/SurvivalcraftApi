var project = Game.GameManager.Project;
var terrain = project?.FindSubsystem<Game.SubsystemTerrain>();
var players = project?.FindSubsystem<Game.SubsystemPlayers>();

int playerIndex = __PLAYER_INDEX__;
if (playerIndex < 0) {
    playerIndex = players?.PlayersData.FirstOrDefault(player => player.IsMainPlayer)?.PlayerIndex ?? -1;
}

var player = players?.PlayersData.FirstOrDefault(data => data.PlayerIndex == playerIndex);
var body = player?.ComponentPlayer?.ComponentBody;
var updater = terrain?.TerrainUpdater;

if (terrain == null || updater == null || player == null || body == null) {
    return string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "ready=False;playerIndex={0};hasTerrain={1};hasLocation=False",
        playerIndex,
        terrain != null);
}

bool hasLocation = updater.UpdateLocations.TryGetValue(playerIndex, out Game.TerrainUpdater.UpdateLocation location);
float radius = __RADIUS__f;
Engine.Vector2 center = hasLocation ? location.Center : body.Position.XZ;
Game.TerrainChunk[] chunks = terrain.Terrain.AllocatedChunks;

var chunksInRadius = chunks
    .Where(chunk => Engine.Vector2.DistanceSquared(chunk.Center, center) <= radius * radius)
    .OrderBy(chunk => chunk.Coords.X)
    .ThenBy(chunk => chunk.Coords.Y)
    .ToList();
var loadedChunksInRadius = chunksInRadius
    // 服务端新生成区块没有设置 IsLoaded；客户端则必须已收到服务端同步数据。
    .Where(chunk => chunk.ThreadState > Game.TerrainChunkState.InvalidContents4
        && (!Game.NetworkManager.IsClientRunning || chunk.IsLoaded))
    .ToList();
var loadedChunks = chunks
    .Where(chunk => chunk.ThreadState > Game.TerrainChunkState.InvalidContents4
        && (!Game.NetworkManager.IsClientRunning || chunk.IsLoaded))
    .ToList();

string loadedCoordinates = string.Join(
    "|",
    loadedChunksInRadius.Select(chunk => string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "{0},{1}",
        chunk.Coords.X,
        chunk.Coords.Y)));

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "ready=True;playerIndex={0};bodyX={1:R};bodyY={2:R};bodyZ={3:R};hasLocation={4};locationX={5:R};locationZ={6:R};visibility={7:R};content={8:R};totalAllocated={9};totalLoaded={10};allocated={11};loaded={12};loadedCoordinates={13}",
    playerIndex,
    body.Position.X,
    body.Position.Y,
    body.Position.Z,
    hasLocation,
    hasLocation ? location.Center.X : 0f,
    hasLocation ? location.Center.Y : 0f,
    hasLocation ? location.VisibilityDistance : 0f,
    hasLocation ? location.ContentDistance : 0f,
    chunks.Length,
    loadedChunks.Count,
    chunksInRadius.Count,
    loadedChunksInRadius.Count,
    loadedCoordinates);
