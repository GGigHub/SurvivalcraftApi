using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkDamage {
        public static void OnHitValueParticleSystemAdded(ParticleSystemBase particleSystem) {
            if (!NetworkManager.IsClient) {
                if (particleSystem is HitValueParticleSystem hitValueParticleSystem) {
                    HitValueParticleSystem.Particle particle = hitValueParticleSystem.Particles[0];
                    NetworkManager.QueuePacket(new HitValuePacket(particle.Position, particle.Velocity, particle.BaseColor, particle.Text));
                }
            }
        }

        public static void OnDamage(ComponentDamage damage, float fallbackValue = 0f) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new DamageSyncPacket(damage));
            }
        }
    }
}
