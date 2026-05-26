using Engine;

namespace Game.Network;

public class ClientRemovePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.ClientRemove;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
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
        if (NetworkService.Clients.ContainsKey(m_clientId)) {
            SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            Client client = NetworkService.Clients[m_clientId];
            client.State = NetworkState.NotConnected;
            PlayerData playerData = subsystemPlayers.PlayersData.Find(pd => pd.NetworkGuid == client.NetworkGuid);
            if (playerData != null && !playerData.IsMainPlayer) {
                subsystemPlayers.MakePlayerOffline(playerData.NetworkGuid);
            }
            NetworkService.Clients.Remove(m_clientId);
        }
    }
}

