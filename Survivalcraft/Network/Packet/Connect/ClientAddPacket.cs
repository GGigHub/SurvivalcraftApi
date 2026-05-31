using Engine;

namespace Game.Network;

public class ClientAddPacket : ClientPacketBase{
    public override byte ID => (byte)PacketType.ClientAdd;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
    public Client m_client;

    public ClientAddPacket() { }

    public ClientAddPacket(byte id, Guid guid, string nickname) {
        m_client = new Client(id, guid, nickname);
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeClient(archive, To, ref m_client);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        NetworkService.AddClient(new Client(m_client.ID, m_client.NetworkGuid, m_client.Nickname));
    }
}

