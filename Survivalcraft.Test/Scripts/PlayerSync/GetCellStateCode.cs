var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
int x = __X__;
int y = __Y__;
int z = __Z__;
int value = terrain.Terrain.GetCellValue(x, y, z);
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};contents={3};data={4}",
    x,
    y,
    z,
    Game.Terrain.ExtractContents(value),
    Game.Terrain.ExtractData(value));
