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

        public static void OnPickableRequestSync(SubsystemPickables subsystem, int id) {
            NetworkManager.QueuePacket(new PickableRequestSyncPacket(id));
        }

        public static void OnPickableCreate(SubsystemPickables subsystem, Pickable pickable) {
            NetworkManager.QueuePacket(new PickableCreatePacket(pickable));
        }

        public static void OnPickableDelete(SubsystemPickables subsystem, Pickable pickable) {
            NetworkManager.QueuePacket(new PickableDeletePacket(pickable));
        }
    }
}
