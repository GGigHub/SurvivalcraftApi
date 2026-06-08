using Engine;

namespace Game.Network;

public class ClientRemovePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.ClientRemove;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
    public int m_playerIndex;

    public ClientRemovePacket() { }

    public ClientRemovePacket(int playerIndex) {
        m_playerIndex = playerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerIndex(archive, ref m_playerIndex);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        if (NetworkService.Clients.TryGetValue(m_playerIndex, out ClientSession client)) {
            SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            client.NetworkState = NetworkState.NotConnected;
            PlayerData playerData = subsystemPlayers.PlayersData.Find(pd => pd.PlayerIndex == m_playerIndex);
            if (playerData != null && !playerData.IsMainPlayer) {
                subsystemPlayers.MakePlayerOffline(playerData.NetworkGuid);
            }
            NetworkService.Clients.Remove(m_playerIndex);
        }
    }
}

