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
        if (!isServer || From == null) {
            return;
        }
        From.NetworkState = m_state;
        if (isServer && From != null) {
            var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            var subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();

            NetworkManager.Queue(new StatePacket(From.PlayerIndex, From.NetworkState) { Except = From });

            switch (From.NetworkState) {
                case NetworkState.Connected: {
                        /*
                        byte[] textureData = null;
                        SubsystemNetwork subsystemNetwork = GameManager.Project.FindSubsystem<SubsystemNetwork>();
                        if (subsystemNetwork.MakePlayerOnline(client.NetworkGuid, out PlayerData playerData)) {
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
                        NetworkManager.Queue(new EntityLoadPacket(sendList) { To = From });
                        SubsystemNetwork subsystemNetwork = GameManager.Project.FindSubsystem<SubsystemNetwork>();
                        subsystemNetwork.MakePlayerEntityOnline(From, out _);
                        break;
                    }
                case NetworkState.LoadTerrain:
                    if (From.GetPlayerData()?.ComponentPlayer != null
                        && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                        From.GetPlayerData().ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                    }
                    break;
                case NetworkState.Playing:
                    if (From.GetPlayerData() == null) {
                        //NetworkManager.Server.RemoveClient(From, "错误:该Client没有对应PlayerData");
                    }
                    else if (From.GetPlayerData().ComponentPlayer != null
                        && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                        From.GetPlayerData().ComponentPlayer.ComponentHealth.IsInvulnerable = false;
                    }
                    foreach (Entity entity in GameManager.Project.Entities) {
                        ComponentHealth health = entity.FindComponent<ComponentHealth>();
                        if (health != null && entity.Id > 0) {
                            NetworkManager.Queue(new HealthSyncPacket(health) { To = From });
                        }
                        ComponentDamage damage = entity.FindComponent<ComponentDamage>();
                        if (damage != null && entity.Id > 0) {
                            NetworkManager.Queue(new DamageSyncPacket(damage) { To = From });
                        }
                        foreach (IInventory inventory in entity.FindComponents<IInventory>()) {
                            if (inventory is ComponentClothing clothing) {
                                NetworkManager.Queue(new ClothingSyncPacket(clothing) { To = From });
                            }
                            else {
                                NetworkManager.Queue(new InventorySyncPacket(inventory) { To = From });
                            }
                        }
                    }
                    break;
            }
        }
    }
}

