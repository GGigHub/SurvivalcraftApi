string expected = "__TEXT__";
var particles = Game.GameManager.Project.FindSubsystem<Game.SubsystemParticles>();
var hit = particles.m_particleSystems.Keys.OfType<Game.HitValueParticleSystem>().LastOrDefault(p => p.Particles[0].Text == expected);
string text = hit?.Particles[0].Text ?? "";
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "exists={0};text={1}", hit != null ? 1 : 0, text);
