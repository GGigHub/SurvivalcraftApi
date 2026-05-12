using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ClientStateChangePacket : ClientPacketBase {
    public override byte ID => (byte)PacketType.ClientStateChange;
    public override ClientState MinNeedState => ClientState.NotConnected;
    public byte m_clientId;
    public ClientState m_state;

    public ClientStateChangePacket() { }

    public ClientStateChangePacket(byte id, ClientState clientStage) {
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
        client.State = m_state;
        if (isServer) {
            var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            var subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();

            NetworkManager.QueuePacket(new ClientStateChangePacket(client.ID, client.State) { Except = client });

            switch (client.State) {
                case ClientState.Connected: {
                        byte[] textureData = null;
                        if (subsystemPlayers.MakePlayerOnline(client.PlayerGuid, out PlayerData playerData, out Entity entity)) {
                            if (entity != null) {
                                GameManager.Project.AddEntity(entity);
                            }
                            NetworkManager.QueuePacket(new PlayerAddPacket(playerData) { Except = client });
                        }
                        if (!string.IsNullOrEmpty(subsystemGameInfo.WorldSettings.BlocksTextureName)) {
                            using var s = Storage.OpenFile(BlocksTexturesManager.GetFileName(subsystemGameInfo.WorldSettings.BlocksTextureName), OpenFileMode.Read);
                            textureData = ModsManager.StreamToBytes(s);
                        }
                        byte[] data = NetworkManager.GetNowProject(GameManager.Project);
                        NetworkManager.QueuePacket(new ProjectPacket(textureData, data) { To = client });
                        break;
                    }
                case ClientState.NotConnected:
                    subsystemPlayers.MakePlayerOffline(client.PlayerGuid);
                    break;
                case ClientState.ProjectLoaded: {
                        List<Entity> sendList = [.. GameManager.Project.Entities];
                        NetworkManager.QueuePacket(new EntityLoadPacket(sendList) { To = client });
                        break;
                    }
                case ClientState.LoadTerrain:
                    if (client.PlayerData?.ComponentPlayer != null
                        && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                        client.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                    }
                    break;
                case ClientState.Playing:
                    if (client.PlayerData == null) {
                        NetworkManager.Server.RemoveClient(client, "错误:该Client没有对应PlayerData");
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

