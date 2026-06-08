using Engine;
using GameEntitySystem;

namespace Game.Network;

public class NetworkStateChangePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.NetworkStateChange;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
    public int m_playerIndex;
    public NetworkState m_state;

    public NetworkStateChangePacket() { }

    public NetworkStateChangePacket(int playerIndex, NetworkState clientStage) {
        m_playerIndex = playerIndex;
        m_state = clientStage;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerIndex(archive, ref m_playerIndex);
        archive.Enum(ref m_state);
    }

    public override void Handle(bool isServer) {
        var clientKeyValuePair = NetworkService.Clients.FirstOrDefault(kv => kv.Value.PlayerIndex == m_playerIndex);
        if (clientKeyValuePair.Value == null) {
            return;
        }
        var client = clientKeyValuePair.Value;
        client.NetworkState = m_state;
        if (isServer) {
            var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            var subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();

            NetworkService.QueuePacket(new NetworkStateChangePacket(client.PlayerIndex, client.NetworkState) { ExceptPlayerIndex = client.PlayerIndex });

            switch (client.NetworkState) {
                case NetworkState.Connected: {
                        byte[] textureData = null;
                        if (subsystemPlayers.MakePlayerOnline(client.NetworkGuid, out PlayerData playerData, out Entity entity)) {
                            if (entity != null) {
                                GameManager.Project.AddEntity(entity);
                            }
                            NetworkService.QueuePacket(new PlayerAddPacket(playerData) { Except = client });
                        }
                        if (!string.IsNullOrEmpty(subsystemGameInfo.WorldSettings.BlocksTextureName)) {
                            using var s = Storage.OpenFile(BlocksTexturesManager.GetFileName(subsystemGameInfo.WorldSettings.BlocksTextureName), OpenFileMode.Read);
                            textureData = ModsManager.StreamToBytes(s);
                        }
                        byte[] data = NetworkService.GetNowProject(GameManager.Project);
                        NetworkService.QueuePacket(new ProjectPacket(textureData, data) { To = client });
                        break;
                    }
                case NetworkState.NotConnected:
                    subsystemPlayers.MakePlayerOffline(client.NetworkGuid);
                    break;
                case NetworkState.ProjectLoaded: {
                        List<Entity> sendList = [.. GameManager.Project.Entities];
                        NetworkService.QueuePacket(new EntityLoadPacket(sendList) { To = client });
                        break;
                    }
                case NetworkState.LoadTerrain:
                    if (client.GetPlayerData()?.ComponentPlayer != null
                        && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                        client.GetPlayerData().ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                    }
                    break;
                case NetworkState.Playing:
                    if (client.GetPlayerData() == null) {
                        NetworkService.Server.RemoveClient(client, "错误:该Client没有对应PlayerData");
                    }
                    else if (client.GetPlayerData().ComponentPlayer != null
                        && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                        client.GetPlayerData().ComponentPlayer.ComponentHealth.IsInvulnerable = false;
                    }
                    break;
            }
        }
    }
}

