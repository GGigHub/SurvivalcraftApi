using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkDamage {
        public static void OnDamage(ComponentDamage damage) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new DamageSyncPacket(damage));
            }
        }
    }
}
