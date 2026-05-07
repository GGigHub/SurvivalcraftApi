using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class BehaviorHelpers {
    public static void WithEntity(int entityId, Action<Entity> action) {
        GameManager.Project.FindEntity(entityId, action);
    }

    public static void BroadcastFromServer(Packet packet, bool isServer) {
        if (isServer) {
            packet.Except = packet.From;
            NetworkManager.QueuePacket(packet);
        }
    }
}

