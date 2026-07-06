var entity = Game.GameManager.Project.FindEntity({entityId});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health == null) return -1f;
            health.IsInvulnerable = false;
            health.m_regenerateLifeEnabled = false;
            health.Injure({amount:R}f, null, true, "{cause}");
            return health.Health;