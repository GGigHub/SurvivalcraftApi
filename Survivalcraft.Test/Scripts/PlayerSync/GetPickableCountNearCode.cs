var project = Game.GameManager.Project;
var pickables = project.FindSubsystem<Game.SubsystemPickables>();
var center = new Engine.Vector3(__X__ + 0.5f, __Y__ + 1f, __Z__ + 0.5f);
int count = 0;
foreach (var pickable in pickables.Pickables) {
    if (Game.Terrain.ExtractContents(pickable.Value) != __CONTENTS__) {
        continue;
    }
    if (Engine.Vector3.DistanceSquared(pickable.Position, center) <= 64f) {
        count++;
    }
}
return count;
