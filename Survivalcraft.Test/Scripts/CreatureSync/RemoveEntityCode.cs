var sub = Game.GameManager.Project.FindSubsystem<SubsystemCreatureSpawn>();
foreach (var c in sub.Creatures) { 
    int id = c.Entity.Id;
    Game.GameManager.Project.RemoveEntity(c.Entity, true); 
    return id;
}
return 0;
