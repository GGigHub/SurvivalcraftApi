var project = Game.GameManager.Project;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
int x = __X__;
int y = __Y__;
int z = __Z__;
int pistonValue = terrain.Terrain.GetCellValue(x, y, z);
int face = Game.PistonBlock.GetFace(Game.Terrain.ExtractData(pistonValue));
var direction = Game.CellFace.FaceToPoint3(face);
int blockValue = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index);

for (int i = 1; i <= 9; i++) {
    terrain.ChangeCell(x + direction.X * i, y + direction.Y * i, z + direction.Z * i, 0);
}

terrain.ChangeCell(x + direction.X, y + direction.Y, z + direction.Z, blockValue);

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "offset={0},{1},{2};value={3}",
    direction.X,
    direction.Y,
    direction.Z,
    blockValue);
