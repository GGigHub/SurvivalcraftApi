var project = Game.GameManager.Project;
if (project == null) return "no-project";
var subsystemBlockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
var subsystemDeviceBehavior = project.FindSubsystem<SCIENEW.SubsystemDeviceBlockBehavior>();
var subsystemTerrain = project.FindSubsystem<Game.SubsystemTerrain>();
if (subsystemDeviceBehavior == null) return "no-subsystem";
var origin = new Engine.Point3(@@0@@, @@1@@, @@2@@);
var positions = new Engine.Point3[]
{
    origin,
    origin + new Engine.Point3(1, 0, 0),
    origin + new Engine.Point3(2, 0, 0),
    origin + new Engine.Point3(3, 0, 0),
    origin + new Engine.Point3(4, 0, 0)
};
int created = 0;
for (int i = 0; i < positions.Length; i++)
{
    int value = subsystemTerrain.Terrain.GetCellValue(positions[i].X, positions[i].Y, positions[i].Z);
    if (value > 0 && subsystemBlockEntities.GetBlockEntity(positions[i].X, positions[i].Y, positions[i].Z) == null)
    {
        subsystemDeviceBehavior.OnBlockAdded(value, 0, positions[i].X, positions[i].Y, positions[i].Z);
        if (subsystemBlockEntities.GetBlockEntity(positions[i].X, positions[i].Y, positions[i].Z) != null)
        {
            created++;
        }
    }
}
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "created={0}", created);
