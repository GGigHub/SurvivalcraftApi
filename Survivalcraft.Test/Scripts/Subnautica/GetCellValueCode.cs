var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
return terrain.Terrain.GetCellValue(__X__, __Y__, __Z__).ToString(System.Globalization.CultureInfo.InvariantCulture);