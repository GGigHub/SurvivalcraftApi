using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkDamage {
        public static void OnDamage(ComponentDamage damageComponent, float damage) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new DamageSyncPacket(damageComponent));
            }
        }

        public static void OnHitValueParticleSystemAdded(Attackment attackment, Vector3 position, Vector3 velocity, Color color, string text) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new HitValuePacket(position, velocity, color, text));
            }
        }
    }
}
