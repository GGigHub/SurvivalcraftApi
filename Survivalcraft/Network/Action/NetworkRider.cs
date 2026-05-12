using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkRider {
        public static void OnMounting(ComponentRider rider, ComponentMount mount, bool isRequest) {
            NetworkManager.QueuePacket(new ComponentMountPacket(rider, mount, isRequest));
        }

        public static void OnDismounting(ComponentRider rider, bool isRequest) {
            NetworkManager.QueuePacket(new ComponentMountPacket(rider, isRequest));
        }
    }
}
