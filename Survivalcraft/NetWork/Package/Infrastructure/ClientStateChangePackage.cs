using Engine;

namespace Game.NetWork;

public class ClientStateChangePackage : IPackage {
    public override byte ID => (byte)PackageType.ClientStateChange;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public byte m_clientId;
    public ClientStage m_state;

    public ClientStateChangePackage() { }

    public ClientStateChangePackage(byte id, ClientStage clientStage) {
        m_clientId = id;
        m_state = clientStage;
    }

    public override void Serialize(PackageSerializer archive) {
        ClientPackageSerialization.SerializeClientId(archive, ref m_clientId);
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