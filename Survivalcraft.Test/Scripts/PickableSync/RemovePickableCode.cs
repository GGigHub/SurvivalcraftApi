var sub = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>();
            var pickable = sub.Pickables.FirstOrDefault(p => p.Id == __PICKABLE_ID__);
            if (pickable == null) return 0;
            pickable.ToRemove = true;
            sub.Update(0f);
            return __PICKABLE_ID__;
