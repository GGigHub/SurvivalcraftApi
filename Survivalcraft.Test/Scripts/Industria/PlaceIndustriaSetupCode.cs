var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
var origin = new Engine.Point3(@@0@@, @@1@@, @@2@@);
int support = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index, 0, 0);
int generator = SCIENEW.DevicesBlockManager.GetDeviceValueByType<SCIENEW.CreativeGeneratorDevice>();
int wire = SCIENEW.DevicesBlockManager.GetDeviceValueByType<SCIENEW.CopperWireDevice>();
int furnace = SCIENEW.DevicesBlockManager.GetDeviceValueByType<SCIENEW.ElectricFurnaceDevice>();
int cell = SCIENEW.DevicesBlockManager.GetDeviceValueByType<SCIENEW.GalvanicCellDevice>();
int tank = SCIENEW.DevicesBlockManager.GetDeviceValueByType<SCIENEW.FluidTankDevice>();
for (int i = 0; i < 5; i++)
{
    terrain.ChangeCell(origin.X + i, origin.Y - 1, origin.Z, support);
}
terrain.ChangeCell(origin.X, origin.Y, origin.Z, generator);
terrain.ChangeCell(origin.X + 1, origin.Y, origin.Z, wire);
terrain.ChangeCell(origin.X + 2, origin.Y, origin.Z, furnace);
terrain.ChangeCell(origin.X + 3, origin.Y, origin.Z, cell);
terrain.ChangeCell(origin.X + 4, origin.Y, origin.Z, tank);
return string.Format("{0},{1},{2},{3},{4}", generator, wire, furnace, cell, tank);
