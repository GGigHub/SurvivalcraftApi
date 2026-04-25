using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game.NetWork.Packages {
    public class EntityPackage : IPackage {
        public byte ID => (byte)PackageType.Entity;
        public Client To { get; set; }
        public Client Except { get; set; }
        public Client From { get; set; }
        public ClientStage MinNeedState => ClientStage.ProjectLoaded;
        public EntityDataList entityDataList;
        public enum EventType {
            Remove,
            LoadList,
            LoadOne,
            RequestSync
        }

        public EventType m_type;
        public int m_entityId;
        public List<int> m_entityIdList;

        public EntityPackage() {
        }
        public EntityPackage(int id) {
            m_type = EventType.Remove;
            m_entityId = id;
        }
        public EntityPackage(List<int> idList) {
            m_type = EventType.RequestSync;
            m_entityIdList = [.. idList];
        }
        public EntityPackage(Entity entity) {
            m_type = EventType.LoadOne;
            entityDataList = GameManager.Project.SaveEntities([entity]);
        }
        public EntityPackage(List<Entity> entities) {
            m_type = EventType.LoadList;
            entityDataList = GameManager.Project.SaveEntities([.. entities]);
        }

        public void WriteData(PackageStreamWriter writer) {
            writer.WriteEnum(m_type);
            switch (m_type) {
                case EventType.LoadOne:
                case EventType.LoadList:
                    writer.WriteEntityLoadList(entityDataList, To);
                    break;
                case EventType.Remove:
                    writer.Write(m_entityId);
                    break;
                case EventType.RequestSync:
                    writer.Write(m_entityIdList.Count);
                    foreach (var e in m_entityIdList) writer.Write(e);
                    break;
            }
        }
        public void ReadData(PackageStreamReader reader) {
            m_type = reader.ReadEnum<EventType>();
            switch (m_type) {
                case EventType.LoadOne:
                case EventType.LoadList:
                    entityDataList = reader.ReadEntityLoadList();
                    break;
                case EventType.Remove:
                    m_entityId = reader.ReadInt32();
                    break;
                case EventType.RequestSync:
                    int m = reader.ReadInt32();
                    m_entityIdList = new List<int>();
                    for (int i = 0; i < m; i++) m_entityIdList.Add(reader.ReadInt32());
                    break;
            }
        }

        public void Handle(bool isServer) {
            if (isServer) {
                return;
            }
            List<Entity> el = new List<Entity>();
            switch (m_type) {
                case EventType.LoadOne:
                case EventType.LoadList:
                    el = GameManager.Project.InitializeAndLoadEntities(entityDataList);
                    // 这里是否应该对重复实体则进行剔除呢？我认为不需要，因为单机也没做
                    // 触发fire事件
                    GameManager.Project.AttachEntities(el, true);
                    break;
                case EventType.Remove:
                    GameManager.Project.FindEntity(m_entityId, (entity) => {
                        GameManager.Project.RemoveEntity(entity, true);
                    });
                    break;
                case EventType.RequestSync:
                    foreach (int e in m_entityIdList) {
                        GameManager.Project.FindEntity(e, entity => 
                            el.Add(entity)
                        );
                    }
                    NetworkManager.QueuePackage(new EntityPackage(el) { To = From });
                    break;
            }
        }

        public static void ConfuseGuid(EntityDataList entityDataList, ValuesDictionary vd, Client to) {
            // 在这里对 vd 进行读取与修正。
            for (int i = 1; i <= entityDataList.EntitiesData.Count; i++) {
                // 每个实体对应一个以 i 为字符串的子字典
                if (!vd.ContainsKey(i.ToString()))
                    continue;

                var entityDict = vd.GetValue<ValuesDictionary>(i.ToString());
                if (!entityDict.ContainsKey("Overrides"))
                    continue;

                var overrides = entityDict.GetValue<ValuesDictionary>("Overrides");

                // 玩家组件在 "Player" 键下
                if (overrides.ContainsKey("Player")) {
                    var playerVD = overrides.GetValue<ValuesDictionary>("Player");

                    // 取回源实体（i-1 对应 entities 的索引）
                    var e = entityDataList.Entities[i - 1];
                    var cp = e.FindComponent<ComponentPlayer>();
                    if (cp != null) {
                        if (cp.PlayerGuid != to.PlayerGuid) {
                            playerVD.SetValue("PlayerGuid", cp.PlayerData.Client.TokenId);
                        }
                    }
                }
            }
        }
    }
}
