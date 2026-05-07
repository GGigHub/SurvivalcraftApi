using Engine;

namespace Game.Network;

public class ClientSyncListPacket : Packet {
    public override byte ID => (byte)PacketType.ClientSyncList;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public List<Client> m_list = [];

    public ClientSyncListPacket() { }

    public ClientSyncListPacket(IEnumerable<Client> clients) {
        m_list = [.. clients];
    }

    public override void Serialize(PacketSerializer archive) {
        byte count = archive.IsWriting ? (byte)m_list.Count : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_list = [];
            for (int i = 0; i < count; i++) {
                Client client = null;
                ClientPacketSerialization.SerializeClient(archive, To, ref client);
                m_list.Add(client);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                Client client = m_list[i];
                ClientPacketSerialization.SerializeClient(archive, To, ref client);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        foreach (var client in m_list) {
            if (client.ID == 0) {
                client.Peer = From.Peer;
                client.Peer.Tag = client;
                NetworkManager.Client.Server = From;
            }
            else if (client.TokenId == NetworkManager.Client.TokenId) {
                NetworkManager.Client.Self = client;
            }
            NetworkManager.AddClient(client);
        }
        if (NetworkManager.Self == null) {
            throw new Exception("Cannot find Self In Client List");
        }
        NetworkManager.Client.CurrentStage = NetworkStage.Connected;
    }
}

