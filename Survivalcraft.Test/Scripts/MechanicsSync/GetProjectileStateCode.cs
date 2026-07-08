var subsystem = Game.GameManager.Project.FindSubsystem<Game.SubsystemProjectiles>();
int count = subsystem.Projectiles.Count(p => p.Value == __VALUE__);
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "count={0};value={1}", count, __VALUE__);
