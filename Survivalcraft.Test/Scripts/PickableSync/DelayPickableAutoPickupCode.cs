var sub = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>();
            var pickable = sub.Pickables.FirstOrDefault(p => p.Id == __PICKABLE_ID__);
            if (pickable != null) {
                pickable.CreationTime = sub.m_subsystemGameInfo.TotalElapsedGameTime + 60.0;
                pickable.FlyToPosition = pickable.Position + new Engine.Vector3(100f, 0f, 0f);
            }
