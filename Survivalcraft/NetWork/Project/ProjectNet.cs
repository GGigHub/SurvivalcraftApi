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

    /// <summary>
    /// Entity包的加载方法
    /// </summary>
    /// <param name="entityDataList"></param>
    /// <returns></returns>
    public List<Entity> LoadEntitiesAll(EntityDataList entityDataList) {
        //List<Entity> list = new List<Entity>(entityDataList.EntitiesData.Count);
        List<Entity> list = new List<Entity>();
        Dictionary<int, Entity> dictionary = new Dictionary<int, Entity>();
        IdToEntityMap idToEntityMap = new IdToEntityMap(dictionary);
        var tmpList = new List<EntityData>();
        var toRemovePlayer = new List<EntityData>();
        for (int i = 0; i < entityDataList.EntitiesData.Count; i++) {
            EntityData entitiesDatum = entityDataList.EntitiesData[i];
            try {
                if (entitiesDatum.ValuesDictionary.ContainsKey("Player")) {
                    var palyerValue = entitiesDatum.ValuesDictionary.GetValue<ValuesDictionary>("Player");
                    if (palyerValue.ContainsKey("PlayerGuid")) {
                        Guid playerGuid = palyerValue.GetValue<Guid>("PlayerGuid", NetworkManager.Self.PlayerGuid);
                        PlayerData playerData = subsystemPlayers.PlayersData.Find((PlayerData d) => d.PlayerGUID == playerGuid);
                        if (playerData == null) {
                            toRemovePlayer.Add(entitiesDatum);
                            continue;
                        }
                    }
                }
                Entity entity = new Entity(this, entitiesDatum.ValuesDictionary);
                list.Add(entity);
                entity.Id = (ushort)entitiesDatum.Id;
                dictionary.Add(entitiesDatum.Id, entity);
                tmpList.Add(entitiesDatum);
            }
            catch (Exception innerException) {
                throw new Exception($"Error creating entity from template \"{entitiesDatum.ValuesDictionary.DatabaseObject.Name}\".", innerException);
            }
        }
        foreach (var rp in toRemovePlayer) {
            entityDataList.EntitiesData.Remove(rp);
        }
        int num = 0;
        foreach (EntityData entitiesDatum2 in tmpList) {
            list[num].publicLoadEntity(entitiesDatum2.ValuesDictionary, idToEntityMap);
            num++;
        }
        return list;
    }

    /// <summary>
    /// Entity包的保存方法
    /// </summary>
    /// <param name="entities"></param>
    /// <returns></returns>
    public EntityDataList SaveEntitiesAll(IEnumerable<Entity> entities) {
        Dictionary<Entity, int> dictionary2 = new Dictionary<Entity, int>();
        EntityToIdMap entityToIdMap = new EntityToIdMap(dictionary2);
        foreach (Entity key in entities) {
            dictionary2.Add(key, key.Id);
        }
        EntityDataList entityDataList = new EntityDataList();
        entityDataList.EntitiesData = new List<EntityData>();
        foreach (Entity key2 in entities) {
            EntityData entityData = new EntityData();
            entityData.Id = key2.Id;
            entityData.ValuesDictionary = new ValuesDictionary();
            entityData.ValuesDictionary.DatabaseObject = key2.ValuesDictionary.DatabaseObject;
            key2.InternalSaveEntity(entityData.ValuesDictionary, entityToIdMap);
            entityDataList.EntitiesData.Add(entityData);
        }
        return entityDataList;
    }
}

