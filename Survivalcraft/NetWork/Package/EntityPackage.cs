using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork.Packages {
    public class EntityPackage : IPackage {
        public override byte ID => (byte)PackageType.Entity;
        public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
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

        public override void Serialize(PackageSerializer archive) {
            archive.Enum(ref m_type);
            switch (m_type) {
                case EventType.LoadOne:
                case EventType.LoadList:
                    archive.EntityLoadList(ref entityDataList, To);
                    break;
                case EventType.Remove:
                    archive.Value(ref m_entityId);
                    break;
                case EventType.RequestSync:
                    int count = archive.IsWriting ? m_entityIdList.Count : 0;
                    archive.Value(ref count);
                    if (archive.IsReading) {
                        m_entityIdList = [];
                        for (int i = 0; i < count; i++) {
                            int entityId = 0;
                            archive.Value(ref entityId);
                            m_entityIdList.Add(entityId);
                        }
                    }
                    else {
                        for (int i = 0; i < count; i++) {
                            int entityId = m_entityIdList[i];
                            archive.Value(ref entityId);
                        }
                    }
                    break;
            }
        }

        public override void Handle(bool isServer) {
            if (isServer) {
                return;
            }
            List<Entity> el = new List<Entity>();
            switch (m_type) {
                case EventType.LoadOne:
                case EventType.LoadList:
                    el = GameManager.Project.InitializeAndLoadEntities(entityDataList);
                    GameManager.Project.AttachEntities(el, true);
                    break;
                case EventType.Remove:
                    GameManager.Project.FindEntity(m_entityId, entity => {
                        GameManager.Project.RemoveEntity(entity, true);
                    });
                    break;
                case EventType.RequestSync:
                    foreach (int e in m_entityIdList) {
                        GameManager.Project.FindEntity(e, entity => el.Add(entity));
                    }
                    NetworkManager.QueuePackage(new EntityPackage(el) { To = From });
                    break;
            }
        }

        public static void ConfuseGuid(EntityDataList entityDataList, ValuesDictionary vd, Client to) {
            for (int i = 1; i <= entityDataList.EntitiesData.Count; i++) {
                if (!vd.ContainsKey(i.ToString()))
                    continue;

                var entityDict = vd.GetValue<ValuesDictionary>(i.ToString());
                if (!entityDict.ContainsKey("Overrides"))
                    continue;

                var overrides = entityDict.GetValue<ValuesDictionary>("Overrides");

                if (overrides.ContainsKey("Player")) {
                    var playerVD = overrides.GetValue<ValuesDictionary>("Player");
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
