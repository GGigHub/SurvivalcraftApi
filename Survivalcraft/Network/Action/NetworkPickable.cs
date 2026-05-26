using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkPickable {
        public static void OnPickableAttract(Pickable pickable) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new PickableSetFlyToPositionPacket(pickable));
            }
        }

        public static void OnPickableRequestSync(int id) {
            NetworkService.QueuePacket(new PickableRequestSyncPacket(id));
        }

        public static void OnPickableCreate(Pickable pickable) {
            NetworkService.QueuePacket(new PickableCreatePacket(pickable));
        }

        public static void OnPickableDelete(Pickable pickable) {
            NetworkService.QueuePacket(new PickableDeletePacket(pickable));
        }
    }
}
