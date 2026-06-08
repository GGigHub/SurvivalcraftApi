using Engine;

namespace Game.Network;

public class ClientSyncListPacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.ClientSyncList;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
    public List<ClientSession> m_list = [];

    public ClientSyncListPacket() { }

    public ClientSyncListPacket(IEnumerable<ClientSession> clients) {
        m_list = [.. clients];
    }

    public override void Serialize(PacketSerializer archive) {
        byte count = archive.IsWriting ? (byte)m_list.Count : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_list = [];
            for (int i = 0; i < count; i++) {
                int playerIndex = 0;
                Guid guid = Guid.Empty;
                SerializePlayerInfo(archive, ref playerIndex, ref guid);
                m_list.Add(new ClientSession { PlayerIndex = playerIndex, NetworkGuid = guid });
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                ClientSession client = m_list[i];
                int playerIndex = client.PlayerIndex;
                Guid guid = client.NetworkGuid;
                SerializePlayerInfo(archive, ref playerIndex, ref guid);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        foreach (var client in m_list) {
            if (client.PlayerIndex == 0) {
                NetworkService.Client.Server = Connection;
            }
            else if (client.NetworkGuid == NetworkService.Client.Self.NetworkGuid) {
                NetworkService.Client.Self = client;
            }
            NetworkService.AddClient(client);
        }
        if (NetworkService.Client.Self == null) {
            throw new Exception("Cannot find Self In Client List");
        }
        NetworkService.Client.Self.NetworkState = NetworkState.Connected;
    }
}

