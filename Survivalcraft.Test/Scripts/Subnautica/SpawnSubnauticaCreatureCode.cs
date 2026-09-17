var spawn = Game.GameManager.Project.FindSubsystem<Game.SubsystemCreatureSpawn>();
var entity = spawn.SpawnCreature("__TEMPLATE__", new Engine.Vector3(__X__, __Y__, __Z__), false);
return entity != null ? entity.Id : 0;