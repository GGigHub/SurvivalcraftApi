using System.Reflection;
using Engine;
using Engine.Serialization;
using Game.NetWork.Packages;
using GameEntitySystem;
using TemplatesDatabase;


namespace Game.NetWork;

public class ProjectNet : Project {
    private SubsystemPlayers subsystemPlayers;
    private SubsystemGameInfo subsystemGameInfo;

    public static ProjectNet Project { get; set; }

    public bool SendToClientMode = false;

    public Dictionary<int, Entity> EntityMaps = new Dictionary<int, Entity>();

    public ProjectNet(GameDatabase gameDatabase, ProjectData projectData) : base(gameDatabase, projectData) {
        subsystemPlayers = FindSubsystem<SubsystemPlayers>();
        subsystemGameInfo = FindSubsystem<SubsystemGameInfo>();

        if (NetworkManager.WorkType == WorkType.Client) {
            NetworkManager.OnClientStateChanged += (c) => {
                switch (c.State) {
                    case ClientStage.NotConnected: {
                            var playerData = subsystemPlayers.PlayersData.Find(pd => pd.PlayerGUID == c.PlayerGuid);
                            if (playerData != null && !playerData.IsMainPlayer) {
                                subsystemPlayers.MakePlayerOffline(playerData.PlayerGUID);
                            }
                            break;
                        }
                }
            };
            SettingsManager.VisibilityRange = Math.Min(256, SettingsManager.VisibilityRange);
        }
        else if (NetworkManager.WorkType == WorkType.Server) {
            NetworkManager.OnClientStateChanged += OnClientStateChanged;
        }

        if (NetworkManager.WorkType == WorkType.Client) {
            NetworkManager.QueuePackage(new ClientPackage(NetworkManager.Self.ID, ClientStage.ProjectLoaded));
        }
        //开启包处理
        NetworkManager.TurnOnPackageHanlde(this);

        NextEntityID = projectData.NextEntityID;
    }

    /// <summary>
    /// 客户端连接状态变更
    /// </summary>
    /// <param name="c"></param>
    public void OnClientStateChanged(Client c) {
        if (NetworkManager.WorkType == WorkType.Server) {
            NetworkManager.QueuePackage(new ClientPackage(c.ID, c.State) { Except = c });

            switch (c.State) {
                case ClientStage.Connected: {
                        byte[] textureData = null;
                        if (subsystemPlayers.MakePlayerOnline(c.PlayerGuid, out PlayerData playerData, out Entity entity)) {
                            c.CachePlayerEntity = entity;
                            foreach (var client in NetworkManager.Clients) {
                                if (client.Value != NetworkManager.Self && client.Value != c) {
                                    NetworkManager.QueuePackage(new PlayerDataPackage(playerData, true) { To = client.Value });
                                }
                            }
                        }
                        if (!string.IsNullOrEmpty(subsystemGameInfo.WorldSettings.BlocksTextureName)) {
                            using (var s = Storage.OpenFile(BlocksTexturesManager.GetFileName(subsystemGameInfo.WorldSettings.BlocksTextureName), OpenFileMode.Read)) {
                                textureData = ModsManager.StreamToBytes(s);
                            }
                        }
                        byte[] data = NetworkManager.GetNowProject(this, c);
                        NetworkManager.QueuePackage(new ProjectPackage(textureData, data) { To = c });
                        break;
                    }
                case ClientStage.NotConnected: {
                        subsystemPlayers.MakePlayerOffline(c.PlayerGuid);
                        GC.Collect();
                        break;
                    }
                case ClientStage.ProjectLoaded: {
                        List<Entity> sendList = [.. m_entities.Keys];
                        //向客户端广播实体列表
                        NetworkManager.QueuePackage(new EntityPackage(sendList) { To = c });
                        //如果有实体，添加到Project中并广播给其它客户端
                        if (c.CachePlayerEntity != null) {
                            AddEntity(c.CachePlayerEntity);
                            c.CachePlayerEntity = null;
                        }
                        break;
                    }
                case ClientStage.LoadTerrain:
                    GC.Collect();
                    //进入无敌状态
                    if (c.PlayerData != null && c.PlayerData.ComponentPlayer != null) {
                        if (FindSubsystem<SubsystemGameInfo>().WorldSettings.GameMode != GameMode.Creative) {
                            c.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                        }
                    }
                    break;
                case ClientStage.Playing:
                    if (c.PlayerData == null) {
                        NetworkManager.Server.RemoveClient(c, "未知错误！该Client没有对应PlayerData");
                    }
                    else {
                        //解除无敌状态
                        if (c.PlayerData.ComponentPlayer != null) {
                            if (FindSubsystem<SubsystemGameInfo>().WorldSettings.GameMode != GameMode.Creative) {
                                c.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
                            }
                        }
                    }
                    break;
            }
        }
        else {
            switch (c.State) {
                case ClientStage.NotConnected:
                    subsystemPlayers.MakePlayerOffline(c.PlayerGuid);
                    break;
            }
        }
    }
}

