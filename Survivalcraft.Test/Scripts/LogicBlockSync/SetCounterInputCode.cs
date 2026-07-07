var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
int x = __X__;
int y = __Y__;
int z = __Z__;
bool powered = __POWERED__;

if (powered) {
    int batteryData = Game.BatteryBlock.SetVoltageLevel(0, 15);
    int batteryValue = Game.Terrain.MakeBlockValue(Game.BatteryBlock.Index, 0, batteryData);
    terrain.ChangeCell(x + 1, y - 1, z, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index));
    terrain.ChangeCell(x + 1, y, z, batteryValue);
    return "powered";
}

terrain.ChangeCell(x + 1, y, z, 0);
return "cleared";
