var entity = Game.GameManager.Project.FindEntity({0});
            var damage = entity?.FindComponent<Game.ComponentDamage>();
            var body = entity?.FindComponent<Game.ComponentBody>();
            if (damage == null || body == null) return "missing";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "hitpoints={0:R};density={1:R}", damage.Hitpoints, body.Density);