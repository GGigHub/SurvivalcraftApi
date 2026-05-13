using Engine;

namespace Game.Network;

public static class NetworkSign {
    public static void OnEditSignRequest(SubsystemSignBlockBehavior subsystem, Point3 point) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new EditSignPacket(point));
        }
    }

    public static void OnSignDataChanged(SubsystemSignBlockBehavior subsystem, Point3 point, string[] lines, Color[] colors, string url) {
        NetworkManager.QueuePacket(new SignBlockPacket(point, lines, colors, url));
    }
}
