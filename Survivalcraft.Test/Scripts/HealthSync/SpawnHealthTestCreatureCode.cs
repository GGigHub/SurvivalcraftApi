var project = Game.GameManager.Project;
            var spawn = project.FindSubsystem<Game.SubsystemCreatureSpawn>();
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var position = players.GlobalSpawnPosition + new Engine.Vector3(2f, 0f, 2f);
            var entity = spawn.SpawnCreature("Duck", position, false);
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health != null) {
                health.IsInvulnerable = false;
                health.m_regenerateLifeEnabled = false;
                health.Health = 1f;
                health.m_lastHealth = 1f;
                health.CauseOfDeath = null;
                health.DeathTime = null;
            }
            return entity?.Id ?? 0;