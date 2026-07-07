var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
int x = __X__;
int y = __Y__;
int z = __Z__;

terrain.ChangeCell(x + 1, y, z, 0);
terrain.ChangeCell(x, y, z, 0);

return "removed";
