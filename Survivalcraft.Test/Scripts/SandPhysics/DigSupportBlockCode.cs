var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
int x = __X__;
int y = __Y__;
int z = __Z__;

terrain.DestroyCell(0, x, y - 1, z, 0, false, false);
terrain.TerrainUpdater.UpdateFullySynchronously();

int supportVal = terrain.Terrain.GetCellValue(x, y - 1, z);
int sandVal = terrain.Terrain.GetCellValue(x, y, z);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};supportVal={3};sandVal={4}",
    x,
    y,
    z,
    Game.Terrain.ExtractContents(supportVal),
    Game.Terrain.ExtractContents(sandVal));
