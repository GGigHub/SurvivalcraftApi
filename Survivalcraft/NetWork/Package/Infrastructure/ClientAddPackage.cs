using Engine;

namespace Game.NetWork;

public class ClientAddPackage : IPackage {
    public override byte ID => (byte)PackageType.ClientAdd;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public Client m_client;

    public ClientAddPackage() { }

    public ClientAddPackage(byte id, Guid tokenId, Guid guid, string nickname) {
        m_client = new Client(null, id, tokenId, guid, nickname);
    }

    public override void Serialize(PackageSerializer archive) {
        ClientPackageSerialization.SerializeClient(archive, To, ref m_client);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        NetworkManager.AddClient(new Client(From.Peer, m_client.ID, m_client.TokenId, m_client.PlayerGuid, m_client.Nickname));
    }
}