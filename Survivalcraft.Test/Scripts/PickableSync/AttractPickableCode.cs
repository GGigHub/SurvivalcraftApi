var project = Game.GameManager.Project;
            var sub = project.FindSubsystem<Game.SubsystemPickables>();
            var players = project.FindSubsystem<Game.SubsystemPlayers>();
            var player = players.ComponentPlayers.FirstOrDefault();
            var gatherer = player?.Entity.FindComponent<Game.ComponentPickableGathererPlayer>();
            var pickable = sub.Pickables.FirstOrDefault(p => p.Id == __PICKABLE_ID__);
            if (player == null || gatherer == null || pickable == null) return "missing";
            var gathererPosition = player.ComponentBody.Position + new Engine.Vector3(0f, 0.75f, 0f);
            pickable.Position = gathererPosition + new Engine.Vector3(1.25f, 0f, 0f);
            pickable.Velocity = Engine.Vector3.Zero;
            pickable.Count = 1;
            pickable.CreationTime = sub.m_subsystemGameInfo.TotalElapsedGameTime - 1.0;
            pickable.FlyToPosition = null;
            gatherer.Update(0f);
            var attractedFlyToPosition = pickable.FlyToPosition;
            if (attractedFlyToPosition.HasValue) {
                pickable.Position = attractedFlyToPosition.Value + new Engine.Vector3(12f, 0f, 0f);
                pickable.Velocity = Engine.Vector3.Zero;
                pickable.FlyToPosition = attractedFlyToPosition;
            }
            pickable.CreationTime = sub.m_subsystemGameInfo.TotalElapsedGameTime + 60.0;
            var fly = pickable.FlyToPosition.HasValue ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", pickable.FlyToPosition.Value.X, pickable.FlyToPosition.Value.Y, pickable.FlyToPosition.Value.Z) : "null";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "id={0};value={1};count={2};pos={3:R},{4:R},{5:R};fly={6}", pickable.Id, pickable.Value, pickable.Count, pickable.Position.X, pickable.Position.Y, pickable.Position.Z, fly);
