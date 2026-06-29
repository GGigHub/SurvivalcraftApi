Game.GameManager.Project.FindSubsystem<SubsystemCreatureSpawn>().Creatures
    .Where(c => c.Entity.FindComponent<ComponentPlayer>() == null)
    .Select(c => c.Entity.Id)
    .Distinct()
    .Count()
