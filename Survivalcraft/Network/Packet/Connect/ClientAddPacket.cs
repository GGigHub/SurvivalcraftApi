using Engine;

namespace Game.Network;

public class ClientAddPacket : Packet {
    public override byte ID => (byte)PacketType.ClientAdd;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public Client m_client;

    public ClientAddPacket() { }

    public ClientAddPacket(byte id, Guid tokenId, Guid guid, string nickname) {
        m_client = new Client(null, id, tokenId, guid, nickname);
    }

    public override void Serialize(PacketSerializer archive) {
        ClientHelper.SerializeClient(archive, To, ref m_client);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        NetworkManager.AddClient(new Client(From.Peer, m_client.ID, m_client.TokenId, m_client.PlayerGuid, m_client.Nickname));
    }
}

