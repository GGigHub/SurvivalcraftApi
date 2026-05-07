using Engine;

namespace Game.NetWork;

public class ClientRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.ClientRemove;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public byte m_clientId;

    public ClientRemovePackage() { }

    public ClientRemovePackage(byte id) {
        m_clientId = id;
    }

    public override void Serialize(PackageSerializer archive) {
        ClientPackageSerialization.SerializeClientId(archive, ref m_clientId);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        if (NetworkManager.Clients.ContainsKey(m_clientId)) {
            var client = NetworkManager.Clients[m_clientId];
            client.State = ClientStage.NotConnected;
            NetworkManager.OnClientStateChanged?.Invoke(client);
            NetworkManager.Clients.Remove(m_clientId);
        }
    }
}