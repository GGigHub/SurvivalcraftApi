var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
int brick = Game.BlocksManager.GetBlockIndex(typeof(Game.BricksBlock), false);
int torch = Game.BlocksManager.GetBlockIndex(typeof(Subnautica.SoulTorchBlock), false);
terrain.ChangeCell(__X__, __Y__, __Z__, Game.Terrain.MakeBlockValue(brick));
terrain.ChangeCell(__X__ + 1, __Y__, __Z__, Game.Terrain.MakeBlockValue(brick));
terrain.ChangeCell(__X__, __Y__, __Z__ + 1, Game.Terrain.MakeBlockValue(brick));
terrain.ChangeCell(__X__ + 1, __Y__, __Z__ + 1, Game.Terrain.MakeBlockValue(brick));
terrain.ChangeCell(__X__ + 1, __Y__ + 1, __Z__ + 1, Game.Terrain.MakeBlockValue(torch, 0, 4));
return "brick=" + brick + ";torch=" + torch;