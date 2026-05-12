using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkPickable {
        public static void OnPickableAttract(Pickable pickable) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new PickableSetFlyToPositionPacket(pickable));
            }
        }
    }
}
