using Engine;
using GameEntitySystem;

namespace Game.Network;

public class NetworkStateChangePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.NetworkStateChange;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
    public byte m_clientId;
    public NetworkState m_state;

    public NetworkStateChangePacket() { }

    public NetworkStateChangePacket(byte id, NetworkState clientStage) {
        m_clientId = id;
        m_state = clientStage;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeClientId(archive, ref m_clientId);
        archive.Enum(ref m_state);
    }

    public override void Handle(bool isServer) {
        if (!NetworkService.Clients.ContainsKey(m_clientId)) {
            return;
        }
        var client = NetworkService.Clients[m_clientId];
        client.State = m_state;
        if (isServer) {
            var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            var subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();

            NetworkService.QueuePacket(new NetworkStateChangePacket(client.ID, client.State) { Except = client });

            switch (client.State) {
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
                    if (client.PlayerData?.ComponentPlayer != null
                        && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                        client.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                    }
                    break;
                case NetworkState.Playing:
                    if (client.PlayerData == null) {
                        NetworkService.Server.RemoveClient(client, "错误:该Client没有对应PlayerData");
                    }
                    else if (client.PlayerData.ComponentPlayer != null
                        && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                        client.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
                    }
                    break;
            }
        }
    }
}

