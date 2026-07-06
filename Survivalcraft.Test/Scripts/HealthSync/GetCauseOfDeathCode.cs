var entity = Game.GameManager.Project.FindEntity({entityId});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            return health?.CauseOfDeath ?? "";