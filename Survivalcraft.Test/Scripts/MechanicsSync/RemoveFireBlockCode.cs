var fire = Game.GameManager.Project.FindSubsystem<Game.SubsystemFireBlockBehavior>();
var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
for (int i = 0; i < 5; i++) {
    Engine.Point3 offset = Game.CellFace.FaceToPoint3(i);
    int fx = __X__ + offset.X;
    int fy = __Y__ + offset.Y;
    int fz = __Z__ + offset.Z;
    fire.RemoveFire(fx, fy, fz);
    if (Game.Terrain.ExtractContents(terrain.Terrain.GetCellValue(fx, fy, fz)) == Game.FireBlock.Index) {
        terrain.ChangeCell(fx, fy, fz, 0);
    }
}
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0};y={1};z={2};onFire={3}", __X__, __Y__, __Z__, fire.IsCellOnFire(__X__, __Y__, __Z__) ? 1 : 0);
