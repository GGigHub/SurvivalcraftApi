var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var basePosition = player?.ComponentBody.Position ?? players.GlobalSpawnPosition;
int x = Game.Terrain.ToCell(basePosition.X) + 12;
int y = Game.Terrain.ToCell(basePosition.Y) + 1;
int z = Game.Terrain.ToCell(basePosition.Z) + 12;
var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
for (int dx = -1; dx <= 1; dx++) {
    for (int dz = -1; dz <= 1; dz++) {
        terrain.ChangeCell(x + dx, y, z + dz, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index));
    }
}
var explosions = project.FindSubsystem<Game.SubsystemExplosions>();
explosions.AddExplosion(x, y, z, 20f, false, true);
explosions.Update(0.1f);
int cells = explosions.m_explosionParticleSystem.m_particlesByPoint.Count(p => System.Math.Abs(p.Key.X - x) <= 3 && System.Math.Abs(p.Key.Y - y) <= 3 && System.Math.Abs(p.Key.Z - z) <= 3);
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0};y={1};z={2};cells={3}", x, y, z, cells);
