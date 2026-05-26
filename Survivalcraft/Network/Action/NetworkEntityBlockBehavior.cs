using Engine;

namespace Game.Network;

public static class NetworkEntityBlockBehavior {
    public static void OnOpenInventoryRequest(SubsystemEntityBlockBehavior subsystem, Point3 point) {
        if (NetworkService.IsClient) {
            NetworkService.QueuePacket(new OpenInventoryByPointPacket(point));
        }
    }
}
