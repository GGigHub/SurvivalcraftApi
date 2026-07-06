var entity = Game.GameManager.Project.FindEntity({0});
            var health = entity?.FindComponent<Game.ComponentHealth>();
            if (health != null) {
                health.m_regenerateLifeEnabled = true;
                // Use a periodic task to forcefully inject healing over time so it bypasses any creative mode/sleep restrictions
                // and naturally triggers OnHealthHealUpdate which queues the network packet.
                Action loop = null;
                loop = () => {
                    if (health.Health < 1f) {
                        health.Heal(0.02f);
                        health.Update(0f);
                        Engine.Time.QueueTimeDelayedExecution(Engine.Time.RealTime + 0.1, loop);
                    }
                };
                loop();
            }
            return true;