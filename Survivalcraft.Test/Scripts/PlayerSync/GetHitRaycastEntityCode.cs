var project = Game.GameManager.Project;
var player = project.FindSubsystem<Game.SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
if (player == null) return "missing";

player.ComponentCreatureModel.Update(0f);
var ray = new Engine.Ray3(
    new Engine.Vector3(__PX__f, __PY__f, __PZ__f),
    new Engine.Vector3(__DX__f, __DY__f, __DZ__f));
var hit = player.ComponentMiner.Raycast<Game.BodyRaycastResult>(
    ray,
    Game.RaycastMode.Interaction,
    reach: 2f);
if (!hit.HasValue || hit.Value.ComponentBody == null) return "missing";
return hit.Value.ComponentBody.Entity.Id;
