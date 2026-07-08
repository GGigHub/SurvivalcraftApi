var project = Game.GameManager.Project;
var projectiles = project.FindSubsystem<Game.SubsystemProjectiles>();
var center = new Engine.Vector3(__X__ + 0.5f, __Y__ + 1f, __Z__ + 0.5f);
int count = 0;
foreach (var projectile in projectiles.Projectiles) {
    if (Game.Terrain.ExtractContents(projectile.Value) != __CONTENTS__) {
        continue;
    }
    if (Engine.Vector3.DistanceSquared(projectile.Position, center) <= 4096f) {
        count++;
    }
}
return count;
