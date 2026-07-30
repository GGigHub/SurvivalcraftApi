var project = Game.GameManager.Project;
var terrain = project?.FindSubsystem<Game.SubsystemTerrain>();
var players = project?.FindSubsystem<Game.SubsystemPlayers>();
var updater = terrain?.TerrainUpdater;

int chunkX = __CHUNK_X__;
int chunkZ = __CHUNK_Z__;
int sampleY = __SAMPLE_Y__;
int playerIndex = __PLAYER_INDEX__;

if (playerIndex < 0) {
    playerIndex = Game.NetworkManager.PlayerIndex;
}
if (playerIndex < 0) {
    playerIndex = Game.NetworkManager.ServerSessions.FirstOrDefault(session => session.PlayerIndex >= 0)?.PlayerIndex ?? -1;
}
if (playerIndex < 0 && players != null) {
    playerIndex = players.PlayersData.FirstOrDefault(player => player.IsMainPlayer)?.PlayerIndex ?? -1;
}

var pairs = new System.Collections.Generic.List<string>();

void Add(string key, object value) {
    string text = System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    pairs.Add(key + "=" + text.Replace(';', ','));
}

Add("isServerRunning", Game.NetworkManager.IsServerRunning);
Add("isClientRunning", Game.NetworkManager.IsClientRunning);
Add("myState", Game.NetworkManager.MyState);
Add("myPlayerIndex", Game.NetworkManager.PlayerIndex);
Add("playerIndex", playerIndex);
Add("sessions", string.Join(",", Game.NetworkManager.ServerSessions.Select(session => session.PlayerIndex + ":" + session.NetworkState)));
Add("hasProject", project != null);
Add("hasTerrain", terrain != null);
Add("hasUpdater", updater != null);

if (updater == null || terrain == null) {
    return string.Join(";", pairs);
}

bool hasUpdateLocation = updater.UpdateLocations.TryGetValue(playerIndex, out Game.TerrainUpdater.UpdateLocation location);
bool hasPendingLocation = updater.m_pendingLocations.TryGetValue(playerIndex, out Game.TerrainUpdater.UpdateLocation? pendingLocation);
Add("hasUpdateLocation", hasUpdateLocation);
Add("hasPendingLocation", hasPendingLocation);
Add("pendingLocationValue", !hasPendingLocation ? "none" : pendingLocation.HasValue ? "set" : "remove");
if (hasUpdateLocation) {
    Add("locationCenterX", location.Center.X);
    Add("locationCenterY", location.Center.Y);
    Add("locationVisibility", location.VisibilityDistance);
    Add("locationContent", location.ContentDistance);
    Add("locationLastCenter", location.LastChunksUpdateCenter.HasValue);
}

var chunk = terrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
Add("chunkX", chunkX);
Add("chunkZ", chunkZ);
Add("sampleY", sampleY);
Add("targetLoaded", chunk != null);
Add("targetThreadState", chunk?.ThreadState.ToString() ?? "null");
Add("targetState", chunk?.State.ToString() ?? "null");
Add("targetIsLoaded", chunk?.IsLoaded ?? false);
Add("targetWasUpgraded", chunk?.WasUpgraded ?? false);
Add("targetWasDowngraded", chunk?.WasDowngraded ?? false);

var targetCenter = new Engine.Vector2(
    (chunkX + 0.5f) * Game.TerrainChunk.Size,
    (chunkZ + 0.5f) * Game.TerrainChunk.Size);
Add("targetInRange", hasUpdateLocation && Game.TerrainUpdater.IsChunkInRange(targetCenter, ref location));

Game.TerrainChunk[] allocatedChunks = terrain.Terrain.AllocatedChunks;
int allocatedInRange = 0;
int readyInRange = 0;
int notReadyInRange = 0;
if (hasUpdateLocation) {
    foreach (Game.TerrainChunk allocatedChunk in allocatedChunks) {
        if (!Game.TerrainUpdater.IsChunkInRange(allocatedChunk.Center, ref location)) {
            continue;
        }
        allocatedInRange++;
        if (allocatedChunk.ThreadState > Game.TerrainChunkState.InvalidContents4) {
            readyInRange++;
        }
        else {
            notReadyInRange++;
        }
    }
}
Add("allocatedCount", allocatedChunks.Length);
Add("allocatedInRange", allocatedInRange);
Add("readyInRange", readyInRange);
Add("notReadyInRange", notReadyInRange);

bool hasPendingQueue = updater.waitChunkSetList.TryGetValue(playerIndex, out System.Collections.Generic.HashSet<Engine.Point2> pendingChunks);
bool hasSentSet = updater.sentChunkSetList.TryGetValue(playerIndex, out System.Collections.Generic.HashSet<Engine.Point2> sentChunks);
Add("hasPendingQueue", hasPendingQueue);
Add("pendingCount", pendingChunks?.Count ?? 0);
Add("targetPending", pendingChunks?.Contains(new Engine.Point2(chunkX, chunkZ)) ?? false);
Add("hasSentSet", hasSentSet);
Add("sentCount", sentChunks?.Count ?? 0);
Add("targetSent", sentChunks?.Contains(new Engine.Point2(chunkX, chunkZ)) ?? false);

int cellX = (chunkX << Game.TerrainChunk.SizeBits) + 1;
int cellZ = (chunkZ << Game.TerrainChunk.SizeBits) + 1;
int value = terrain.Terrain.GetCellValue(cellX, sampleY, cellZ);
Add("sampleValue", Game.Terrain.ReplaceLight(value, 0));
Add("sampleContents", Game.Terrain.ExtractContents(value));
Add("sampleData", Game.Terrain.ExtractData(value));

return string.Join(";", pairs);
