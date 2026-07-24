try {
    var project = Game.GameManager.Project;
    var subsystemSpawn = project.FindSubsystem<Game.SubsystemSpawn>();
    var players = project.FindSubsystem<Game.SubsystemPlayers>();
    var spawnPos = players.GlobalSpawnPosition + new Engine.Vector3(2f, 1f, 2f);

    int targetId = 63;
    var data1 = new Game.SpawnEntityData {
        TemplateName = "Duck",
        Position = spawnPos,
        EntityId = targetId
    };

    var entity1 = subsystemSpawn.SpawnEntity(data1);

    var data2 = new Game.SpawnEntityData {
        TemplateName = "Duck",
        Position = spawnPos + new Engine.Vector3(1f, 0f, 0f),
        EntityId = targetId
    };

    var entity2 = subsystemSpawn.SpawnEntity(data2);
    if (entity2 == null) {
        return "unable_to_spawn_null";
    }

    if (entity2.Id != targetId) {
        return $"id_reassigned_collision: requested={targetId}, assigned={entity2.Id}";
    }

    return $"spawned_same_id: entity1={entity1?.Id}, entity2={entity2.Id}";
}
catch (System.Exception ex) {
    return ex.ToString();
}
