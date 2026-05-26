using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkOnFire {
        public static void OnSetOnFire(ComponentOnFire onFire, ComponentCreature attacker, float duration) {
            if (!NetworkService.IsClient) {
                NetworkService.QueuePacket(new EntityOnFirePacket(onFire, attacker, duration));
            }
        }
    }
}
