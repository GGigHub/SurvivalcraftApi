var player = Game.GameManager.Project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.First();
var position = player.ComponentBody.Position + new Engine.Vector3(2f, 0f, 2f);
var entity = Game.GameManager.Project.FindSubsystem<SubsystemCreatureSpawn>().SpawnCreature("Duck", position, false);
return entity?.Id ?? 0;
