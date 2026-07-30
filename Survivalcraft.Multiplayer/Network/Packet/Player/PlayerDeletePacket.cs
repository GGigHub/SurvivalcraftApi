using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerDeletePacket : Packet {
    public override byte ID => (byte)PacketType.PlayerDelete;
    public override NetworkState MinNeedState => NetworkState.Connected;

    public override void Serialize(PacketSerializer archive) { }

    public override void Handle(bool isServer) {
        if (isServer) {
            //NetworkManager.Server.RemoveClient(From);
        }
    }
}

