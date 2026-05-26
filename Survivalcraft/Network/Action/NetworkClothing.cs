using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkClothing {
        public static void OnRequestSkin(ComponentClothing clothing, string skinName) {
            if (Time.PeriodicEvent(1.0, 0.0)) {
                if (NetworkService.IsClient) {
                    NetworkService.QueuePacket(new RequestSkinPacket(skinName));
                }
                else if (NetworkService.IsServer) {
                    NetworkService.QueuePacket(new WhoHasSkinPacket(skinName));
                }
            }
        }
    }
}
