var explosions = Game.GameManager.Project.FindSubsystem<Game.SubsystemExplosions>();
int cells = explosions.m_explosionParticleSystem.m_particlesByPoint.Count(p => System.Math.Abs(p.Key.X - __X__) <= 3 && System.Math.Abs(p.Key.Y - __Y__) <= 3 && System.Math.Abs(p.Key.Z - __Z__) <= 3);
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0};y={1};z={2};cells={3}", __X__, __Y__, __Z__, cells);
