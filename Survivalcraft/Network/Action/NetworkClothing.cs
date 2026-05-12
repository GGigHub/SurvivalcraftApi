using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkClothing {
        public static void OnRequestSkin(ComponentClothing clothing, string skinName) {
            if (Time.PeriodicEvent(1.0, 0.0)) {
                if (NetworkManager.IsClient) {
                    NetworkManager.QueuePacket(new RequestSkinPacket(skinName));
                }
                else if (NetworkManager.IsServer) {
                    NetworkManager.QueuePacket(new WhoHasSkinPacket(skinName));
                }
            }
        }
    }
}
