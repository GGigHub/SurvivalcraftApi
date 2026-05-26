using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkHumanModel {
        public static void OnRow(ComponentHumanModel model) {
            NetworkService.QueuePacket(new HumanRowBehaviorPacket(model));
        }
    }
}
