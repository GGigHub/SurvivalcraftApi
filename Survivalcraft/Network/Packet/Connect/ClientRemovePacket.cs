using Engine;

namespace Game.Network;

public class ClientRemovePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.ClientRemove;
    public override ClientStage MinNeedState => ClientStage.NotConnected;
    public byte m_clientId;

    public ClientRemovePacket() { }

    public ClientRemovePacket(byte id) {
        m_clientId = id;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeClientId(archive, ref m_clientId);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        if (NetworkManager.Clients.ContainsKey(m_clientId)) {
            SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            Client client = NetworkManager.Clients[m_clientId];
            client.State = ClientStage.NotConnected;
            PlayerData playerData = subsystemPlayers.PlayersData.Find(pd => pd.PlayerGUID == client.PlayerGuid);
            if (playerData != null && !playerData.IsMainPlayer) {
                subsystemPlayers.MakePlayerOffline(playerData.PlayerGUID);
            }
            NetworkManager.Clients.Remove(m_clientId);
        }
    }
}

