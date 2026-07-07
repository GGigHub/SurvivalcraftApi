var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var position = players.GlobalSpawnPosition + new Engine.Vector3(__OFFSET_X__f, 1f, __OFFSET_Z__f);
int x = (int)System.MathF.Floor(position.X);
int y = (int)System.MathF.Floor(position.Y);
int z = (int)System.MathF.Floor(position.Z);
int value = Game.Terrain.MakeBlockValue(__BLOCK_INDEX__, 0, __INITIAL_DATA__);

terrain.ChangeCell(x, y - 1, z, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
terrain.ChangeCell(x, y, z, value);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "x={0};y={1};z={2};contents={3};data={4}",
    x,
    y,
    z,
    Game.Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z)),
    Game.Terrain.ExtractData(terrain.Terrain.GetCellValue(x, y, z)));
