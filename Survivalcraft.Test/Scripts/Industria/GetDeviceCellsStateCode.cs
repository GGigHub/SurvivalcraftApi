var project = Game.GameManager.Project;
if (project == null) return "no-project";
var subsystemTerrain = project.FindSubsystem<Game.SubsystemTerrain>();
var terrain = subsystemTerrain.Terrain;
var origin = new Engine.Point3(@@0@@, @@1@@, @@2@@);
var positions = new Engine.Point3[]
{
    origin,
    origin + new Engine.Point3(1, 0, 0),
    origin + new Engine.Point3(2, 0, 0),
    origin + new Engine.Point3(3, 0, 0),
    origin + new Engine.Point3(4, 0, 0)
};
var chunk = terrain.GetChunkAtCell(origin.X, origin.Z);
string cells = string.Empty;
for (int i = 0; i < positions.Length; i++)
{
    int value = Game.Terrain.ReplaceLight(terrain.GetCellValue(positions[i].X, positions[i].Y, positions[i].Z), 0);
    cells += string.Format(System.Globalization.CultureInfo.InvariantCulture, "v{0}={1};", i, value);
}
return "loaded=" + (chunk != null && chunk.IsLoaded ? "1" : "0") + ";" + cells;
