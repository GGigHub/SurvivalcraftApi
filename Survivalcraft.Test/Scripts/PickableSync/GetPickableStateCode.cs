var sub = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>();
            var pickable = sub.Pickables.FirstOrDefault(p => p.Id == __PICKABLE_ID__);
            if (pickable == null) return "missing";
            var fly = pickable.FlyToPosition.HasValue ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", pickable.FlyToPosition.Value.X, pickable.FlyToPosition.Value.Y, pickable.FlyToPosition.Value.Z) : "null";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "id={0};value={1};count={2};pos={3:R},{4:R},{5:R};fly={6}", pickable.Id, pickable.Value, pickable.Count, pickable.Position.X, pickable.Position.Y, pickable.Position.Z, fly);
