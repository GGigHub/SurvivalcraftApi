using Engine;
using GameEntitySystem;

namespace Game.Network;

public class StatePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.StateInfo;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
    public int m_playerIndex;
    public NetworkState m_state;

    public StatePacket() { }

    public StatePacket(int playerIndex, NetworkState clientStage) {
        m_playerIndex = playerIndex;
        m_state = clientStage;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerIndex(archive, ref m_playerIndex);
        archive.Enum(ref m_state);
    }

    public override void Handle(bool isServer) {
        var clientKeyValuePair = From;
        if (clientKeyValuePair == null) {
            return;
        }
        var client = clientKeyValuePair;
        client.NetworkState = m_state;
        if (isServer) {
            var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            var subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();

            NetworkManager.Queue(new StatePacket(client.PlayerIndex, client.NetworkState) { Except = client });

            switch (client.NetworkState) {
                case NetworkState.Connected: {
                        /*
                        byte[] textureData = null;
                        SubsystemNetwork subsystemNetwork = GameManager.Project.FindSubsystem<SubsystemNetwork>();
                        if (subsystemNetwork.MakePlayerOnline(client.NetworkGuid, out PlayerData playerData, out Entity entity)) {
                            if (entity != null) {
                                GameManager.Project.AddEntity(entity);
                            }
                            NetworkManager.Queue(new PlayerAddPacket(playerData) { Except = client });
                        }
                        if (!string.IsNullOrEmpty(subsystemGameInfo.WorldSettings.BlocksTextureName)) {
                            using var s = Storage.OpenFile(BlocksTexturesManager.GetFileName(subsystemGameInfo.WorldSettings.BlocksTextureName), OpenFileMode.Read);
                            textureData = ModsManager.StreamToBytes(s);
                        }
                        byte[] data = NetworkManager.GetNowProject(GameManager.Project);
                        NetworkManager.Queue(new ProjectPacket(data) { To = client });
                        */
                        break;
                    }
                case NetworkState.NotConnected:
                    // GameManager.Project.FindSubsystem<SubsystemNetwork>().MakePlayerOffline(client);
                    break;
                case NetworkState.ProjectLoaded: {
                        List<Entity> sendList = [.. GameManager.Project.Entities];
                        NetworkManager.Queue(new EntityLoadPacket(sendList) { To = client });
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
                        //NetworkManager.Server.RemoveClient(client, "错误:该Client没有对应PlayerData");
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

