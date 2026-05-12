using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkSickness {
        public static void OnSicknessSync(ComponentSickness sickness) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new ComponentSicknessPacket(sickness));
            }
        }
    }
}
