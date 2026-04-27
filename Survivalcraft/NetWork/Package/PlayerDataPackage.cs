using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork.Packages;

public class PlayerDataPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerData;
    public enum DataType {
        Create,
        Modify,
        Delete,
        ClientKnownPlayer,
        AddPlayer,
        SetUpdateLocation,
        CloseTime,
        AddNoMsg,
        RemoveNoMsg,
        Bugle,
        Count
    }

    public override ClientStage MinNeedState => ClientStage.Connected;
    public int m_visibility;
    public ValuesDictionary m_vd;
    public Guid m_playerGuid;
    public string m_playerName;
    public string m_skinName;
    public PlayerClass m_playerClass;
    public DataType m_type;
    public EntityDataList m_entity;
    public TerrainUpdater.UpdateLocation m_updateLocation;

    public string m_bugleTitle; // 小喇叭标题
    public string m_bugleContent; // 小喇叭内容
    public int m_playerCount; // 玩家人数

    public PlayerDataPackage() {

    }

    public PlayerDataPackage(PlayerData playerData, bool confusion)
        : this(playerData, DataType.AddPlayer) {
        if (confusion) {
            m_vd.SetValue("PlayerGUID", playerData.Client.TokenId);
        }
    }

    public PlayerDataPackage(PlayerData playerData, DataType dataType) {
        m_vd = new ValuesDictionary();
        m_type = dataType;
        m_playerName = playerData.Name;
        m_skinName = playerData.CharacterSkinName;
        m_playerGuid = playerData.PlayerGUID;
        m_playerClass = playerData.m_playerClass;
        playerData.Save(m_vd);
    }

    public PlayerDataPackage(Guid guid, bool add) {
        m_type = add ? DataType.AddNoMsg : DataType.RemoveNoMsg;
        m_playerGuid = guid;
    }

    public PlayerDataPackage(int time, string msg) {
        m_playerName = msg;
        m_type = DataType.CloseTime;
        m_visibility = time;
    }

    public PlayerDataPackage(TerrainUpdater.UpdateLocation updateLocation) {
        m_updateLocation = updateLocation;
        m_type = DataType.SetUpdateLocation;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        switch (m_type) {
            case DataType.AddPlayer:
            case DataType.Create:
                byte[] messagePack = archive.IsWriting ? m_vd.ToMessagePack() : null;
                archive.Buffer(ref messagePack);
                if (archive.IsReading) {
                    m_vd = new ValuesDictionary();
                    m_vd.ApplyOverridesUseMessagePack(messagePack);
                }
                break;
            case DataType.Modify:
                archive.Value(ref m_playerGuid);
                archive.Value(ref m_playerName);
                archive.Value(ref m_skinName);
                archive.Enum(ref m_playerClass);
                break;
            case DataType.ClientKnownPlayer:
                archive.Value(ref m_playerGuid);
                break;
            case DataType.SetUpdateLocation:
                SerializeUpdateLocation(archive);
                break;
            case DataType.CloseTime:
                archive.Value(ref m_visibility);
                archive.Value(ref m_playerName);
                break;
            case DataType.Bugle:
                archive.Value(ref m_bugleTitle);
                archive.Value(ref m_bugleContent);
                break;
            case DataType.Count:
                archive.Value(ref m_playerCount);
                break;
            case DataType.AddNoMsg:
            case DataType.RemoveNoMsg:
                archive.Value(ref m_playerGuid);
                break;
        }
    }

    private void SerializeUpdateLocation(PackageSerializer archive) {
        if (archive.IsReading) {
            m_updateLocation = new TerrainUpdater.UpdateLocation();
        }
        Vector2 center = m_updateLocation.Center;
        ushort contentDistance = (ushort)m_updateLocation.ContentDistance;
        ushort visibilityDistance = (ushort)m_updateLocation.VisibilityDistance;
        Vector2? lastChunksUpdateCenter = m_updateLocation.LastChunksUpdateCenter;

        archive.Value(ref center);
        archive.Value(ref contentDistance);
        archive.Value(ref visibilityDistance);
        archive.Value(ref lastChunksUpdateCenter);

        m_updateLocation.Center = center;
        m_updateLocation.ContentDistance = contentDistance;
        m_updateLocation.VisibilityDistance = visibilityDistance;
        m_updateLocation.LastChunksUpdateCenter = lastChunksUpdateCenter;
    }

    public override void Handle(bool isServer) {
        if (isServer &&
            m_type != DataType.Create &&
            m_type != DataType.Modify &&
            m_type != DataType.SetUpdateLocation &&
            m_type != DataType.ClientKnownPlayer) {
            return;
        }
        PlayerData playerData = null;
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        switch (m_type) {
            case DataType.Create:
                playerData = new PlayerData(GameManager.Project);
                playerData.Load(m_vd);
                subsystemPlayers.AddPlayerData(playerData);
                // 服务端广播给所有客户端，添加玩家。
                foreach (var client in NetworkManager.Clients) {
                    if (client.Value == NetworkManager.Self) continue;
                    if (client.Value != playerData.Client) {
                        NetworkManager.QueuePackage(new PlayerDataPackage(playerData, true) { To = client.Value });
                    }
                    else {
                        NetworkManager.QueuePackage(new PlayerDataPackage(playerData, false) { To = client.Value });
                    }
                }
                break;
            case DataType.Modify:
                var playerData2 = subsystemPlayers.FindPlayerData(p => { return p.PlayerGUID == m_playerGuid; });
                if (playerData2 != null) {
                    Client client = From;

                    playerData2.CharacterSkinName = m_skinName;
                    playerData2.m_playerClass = m_playerClass;
                    if (isServer) {
                        NetworkManager.QueuePackage(this);
                    }
                }
                break;
            case DataType.Delete:
                NetworkManager.Server.RemoveClient(From);
                break;
            case DataType.AddPlayer:
                // 客户端收到新增玩家广播。
                playerData = new PlayerData(GameManager.Project);
                playerData.Load(m_vd);
                subsystemPlayers.AddPlayerData(playerData);
                NetworkManager.QueuePackage(new PlayerDataPackage(playerData, DataType.ClientKnownPlayer));
                break;
            case DataType.SetUpdateLocation:
                var player = subsystemPlayers.PlayersData.Find(x => x.Client == From);
                if (player != null) {
                    var updater = GameManager.Project.FindSubsystem<SubsystemTerrain>().TerrainUpdater;
                    updater.SetLastChunksUpdateCenter(player.PlayerIndex, m_updateLocation.LastChunksUpdateCenter);
                    updater.SetUpdateLocation(player.PlayerIndex, m_updateLocation.Center, m_updateLocation.VisibilityDistance, m_updateLocation.ContentDistance);
                }
                break;
            case DataType.CloseTime:
                var p3 = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
                if (p3 != null) {
                    p3.ComponentGui.CloseTime = m_visibility;
                    DialogsManager.ShowDialog(null, new MessageDialog("服务器关闭提醒", m_playerName, LanguageControl.Yes, LanguageControl.No, btn => {
                        DialogsManager.HideAllDialogs();
                    }));
                }
                break;
            case DataType.Bugle:
                ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
                if (mainPlayer != null) {
                    if (m_playerGuid == Guid.Empty || (m_playerGuid != Guid.Empty && mainPlayer.PlayerData.PlayerGUID == m_playerGuid)) {
                        DialogsManager.HideAllDialogs();
                        mainPlayer.ComponentHealth.IsInvulnerable = true;
                        m_bugleContent = m_bugleContent.Replace("[n]", "\n").Replace("[e]", " ");
                        DialogsManager.ShowDialog(null, new MessageDialog(m_bugleTitle, m_bugleContent, LanguageControl.Ok, null, btn => {
                            DialogsManager.HideAllDialogs();
                            mainPlayer.ComponentHealth.IsInvulnerable = false;
                        }));
                    }
                }
                break;
            case DataType.Count:
                ComponentPlayer mainPlayer2 = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
                if (mainPlayer2 != null) {
                    int clientPlayerCount = GameManager.Project.FindSubsystem<SubsystemPlayers>().PlayersData.Count;
                    Log.Information($"隐身测试，服务端人数：{m_playerCount}; 客户端人数：{clientPlayerCount}");
                    if (clientPlayerCount != m_playerCount) {
                        ScreensManager.SwitchScreen("NetPlay");
                        GameManager.DisposeProject();
                        NetworkManager.Stop();
                        DialogsManager.ShowDialog(null, new MessageDialog("连接异常", "检测到玩家人数异常，请重新连接服务器", LanguageControl.Ok, null, btn => {
                        }));
                    }
                }
                break;
            case DataType.AddNoMsg:
                GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Add(m_playerGuid.ToString());
                break;
            case DataType.RemoveNoMsg:
                GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Remove(m_playerGuid.ToString());
                break;
        }
    }
}
