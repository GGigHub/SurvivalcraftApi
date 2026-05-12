using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkHealth {
        public static void OnHealthInjured(ComponentHealth health, Injury injury, float amount) {
            if (!NetworkManager.IsClient && amount > 0f) {
                NetworkManager.QueuePacket(
                    new InjureEventPacket(health, injury.Attacker, amount, injury.Cause, injury.IgnoreInvulnerability)
                );
            }
        }

        public static void OnHealthDead(ComponentHealth health) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new HealthSyncPacket(health));
            }
        }

        public static void OnHealthHealUpdate(ComponentHealth health) {
            if (!NetworkManager.IsClient && Time.PeriodicEvent(2.0, 0.0)) {
                NetworkManager.QueuePacket(new HealthSyncPacket(health));
            }
        }

        public static void OnHealthFallDamage(ComponentHealth health, float fallDamage, string cause) {
            ComponentPlayer riderPlayer = health.m_componentCreature.ComponentBody.ChildBodies.Count > 0
                ? health.m_componentCreature.ComponentBody.ChildBodies[0].Entity?.FindComponent<ComponentPlayer>()
                : null;

            bool flagMainRider = riderPlayer != null && riderPlayer.PlayerData.IsMainPlayer;
            bool flagMainPlayer = health.m_componentPlayer != null && health.m_componentPlayer.PlayerData.IsMainPlayer;
            bool flagNoRider = health.m_componentCreature.ComponentBody.ChildBodies.Count == 0;

            if (NetworkManager.IsServer) {
                if (flagMainPlayer || flagMainRider || flagNoRider) {
                    health.Injure(fallDamage, null, false, cause);
                }
            }
            else if (flagMainPlayer || flagMainRider) {
                NetworkManager.QueuePacket(
                    new InjureRequestPacket(health, null, fallDamage, cause, false, InjureRequestPacket.RequestInjureType.Fall)
                );
            }
        }
    }
}
