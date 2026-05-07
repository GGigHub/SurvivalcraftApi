using Engine;

namespace Game.Network;

public class ClientStateChangePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.ClientStateChange;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public byte m_clientId;
    public ClientStage m_state;

    public ClientStateChangePacket() { }

    public ClientStateChangePacket(byte id, ClientStage clientStage) {
        m_clientId = id;
        m_state = clientStage;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeClientId(archive, ref m_clientId);
        archive.Enum(ref m_state);
    }

    public override void Handle(bool isServer) {
        if (!NetworkManager.Clients.ContainsKey(m_clientId)) {
            return;
        }
        var client = NetworkManager.Clients[m_clientId];
        if (client.State != m_state) {
            client.State = m_state;
            NetworkManager.OnClientStateChanged?.Invoke(client);
        }
    }
}

