var fire = Game.GameManager.Project.FindSubsystem<Game.SubsystemFireBlockBehavior>();
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0};y={1};z={2};onFire={3}", __X__, __Y__, __Z__, fire.IsCellOnFire(__X__, __Y__, __Z__) ? 1 : 0);
