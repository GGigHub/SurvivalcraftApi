using Engine;

namespace Game.Network;

public static class NetworkEntityBlockBehavior {
    public static void OnOpenInventoryRequest(SubsystemEntityBlockBehavior subsystem, Point3 point) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new OpenInventoryByPointPacket(point));
        }
    }
}
