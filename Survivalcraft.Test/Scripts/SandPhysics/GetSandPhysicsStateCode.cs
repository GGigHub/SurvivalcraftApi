var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var movingBlocks = project.FindSubsystem<Game.SubsystemMovingBlocks>();
int x = __X__;
int y = __Y__;
int z = __Z__;

var allSets = movingBlocks.MovingBlockSetEnumerable.ToList();
var collapsingSets = allSets.Where(s => s.Id == Game.SubsystemCollapsingBlockBehavior.IdString).ToList();
int activeCount = collapsingSets.Count(s => !s.Stopped);
int stoppedCount = collapsingSets.Count(s => s.Stopped);

int sandLandCell = terrain.Terrain.GetCellValue(x, y - 1, z);
int sandOrigCell = terrain.Terrain.GetCellValue(x, y, z);
int sandIndex = Game.BlocksManager.GetBlockIndex<Game.SandBlock>();

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};totalSets={3};collapsingSets={4};activeSets={5};stoppedSets={6};landContents={7};origContents={8};sandIndex={9}",
    x,
    y,
    z,
    allSets.Count,
    collapsingSets.Count,
    activeCount,
    stoppedCount,
    Game.Terrain.ExtractContents(sandLandCell),
    Game.Terrain.ExtractContents(sandOrigCell),
    sandIndex);
