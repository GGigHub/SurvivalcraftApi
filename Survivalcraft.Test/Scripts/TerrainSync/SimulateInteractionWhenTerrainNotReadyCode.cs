var project = Game.GameManager.Project;
if (project == null) return "no_project";

var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
if (terrain == null || players == null) return "missing_subsystems";

var player = players.ComponentPlayers.FirstOrDefault();
if (player == null) return "no_player";

var camera = player.GameWidget?.ActiveCamera;
if (camera == null) return "no_camera";

// 获取相机/玩家位置所在的区块
Engine.Point2 chunkCoords = Game.Terrain.ToChunk(camera.ViewPosition.XZ);
int chunkX = chunkCoords.X;
int chunkZ = chunkCoords.Y;

// 获取或分配区块，并将其 State 与 ThreadState 均置为 NotLoaded（模拟地形在客户端未就绪）
var chunk = terrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
if (chunk == null) {
    chunk = terrain.Terrain.AllocateChunk(chunkX, chunkZ);
}
chunk.State = Game.TerrainChunkState.NotLoaded;
chunk.ThreadState = Game.TerrainChunkState.NotLoaded;

// 模拟玩家挖掘/交互动作触发的 RequestSynchronousUpdate()
terrain.TerrainUpdater.RequestSynchronousUpdate();

// 调用 PrepareForDrawing() 触发主线程同步更新未就绪区块逻辑
// PrepareForDrawing -> DetermineSynchronousUpdateChunks 检查到 chunk.State < Valid
// 然后进入 while (item.ThreadState < Valid) 循环
// 在客户端模式下，UpdateChunkSingleStep 不会更新 NotLoaded 的 ThreadState，主线程将无限死循环卡死
terrain.TerrainUpdater.PrepareForDrawing(camera);

return "completed_without_freeze";
