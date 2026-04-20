using Engine;
using GameEntitySystem;
using System.Collections.Generic;
using TemplatesDatabase;
namespace Game.NetWork.Packages
{
    public class EntityPackage : IPackage
    {
        public byte ID => (byte)PackageType.Entity;
        public Client To { get; set; }
        public Client Except { get; set; }
        public Client From { get; set; }
        public ClientState MinNeedState => ClientState.ProjectLoaded;
        public List<Entity> m_entities;
        public enum EventType
        {
            Remove,
            LoadList,
            LoadOne,
            RequestSync
        }

        public EventType m_type;
        public ushort m_entityId;
        public List<ushort> m_entityIdList;

        public EntityPackage()
        {
        }
        public EntityPackage(ushort id)
        {
            m_type = EventType.Remove;
            m_entityId = id;
        }
        public EntityPackage(List<ushort> idList)
        {
            m_type = EventType.RequestSync;
            m_entityIdList = [.. idList];
        }
        public EntityPackage(Entity entity)
        {
            m_type = EventType.LoadOne;
            m_entities = [entity];
        }
        public EntityPackage(List<Entity> entities)
        {
            m_type = EventType.LoadList;
            m_entities = [.. entities];
        }

        public void WriteData(PackageStreamWriter writer)
        {
            writer.WriteEnum(m_type);
            switch (m_type)
            {
                case EventType.LoadOne:
                case EventType.LoadList:
                    writer.WriteEntityLoadList(m_entities, To);
                    break;
                case EventType.Remove:
                    writer.Write(m_entityId);
                    break;
                case EventType.RequestSync:
                    writer.Write((ushort)m_entityIdList.Count);
                    foreach (var e in m_entityIdList) writer.Write(e);
                    break;
            }
        }
        public void ReadData(PackageStreamReader reader)
        {
            m_type = reader.ReadEnum<EventType>();
            switch (m_type)
            {
                case EventType.LoadOne:
                case EventType.LoadList:
                    m_entities = reader.ReadEntityLoadList();
                    break;
                case EventType.Remove:
                    m_entityId = reader.ReadUInt16();
                    break;
                case EventType.RequestSync:
                    ushort m = reader.ReadUInt16();
                    m_entityIdList = new List<ushort>();
                    for (int i = 0; i < m; i++) m_entityIdList.Add(reader.ReadUInt16());
                    break;
            }
        }

        public void Handle(ProjectNet projectNet, bool isServer)
        {
            if (isServer)
            {
                return;
            }
            List<Entity> el = new List<Entity>();
            switch (m_type)
            {
                case EventType.LoadOne:
                case EventType.LoadList:
                    foreach (var e in m_entities)
                    {
                        //如果本地ID没有重复的添加，重复了进行替换
                        if (!projectNet.FindEntityById(e.EntityId, e2 =>
                        {
                            projectNet.RemoveEntity(e2, true);
                            projectNet.AddEntity(e);
#if DEBUG
                            Log.Information($"生物替换[{e.EntityId}]");
#endif
                        })) el.Add(e);
                    }
                    projectNet.AddEntities(el);
                    break;
                case EventType.Remove:
                    projectNet.FindEntityById(m_entityId, (entity) =>
                    {
                        projectNet.RemoveEntity(entity, true);
                    });
                    break;
                case EventType.RequestSync:
                    foreach (var e in m_entityIdList)
                    {
                        projectNet.FindEntityById(e, entity =>
                        {
                            el.Add(entity);
                        });
                    }
                    NetworkManager.QueuePackage(new EntityPackage(el) { To = From });
                    break;
            }
        }

        public static void ConfuseGuid(List<Entity> entities, ValuesDictionary vd, EntityDataList entityDataList, Client to)
        {
            // 在这里对 vd 进行读取与修改
            for (int i = 1; i <= entityDataList.EntitiesData.Count; i++)
            {
                // 每个实体对应一个以 i 为字符串的子字典
                if (!vd.ContainsKey(i.ToString()))
                    continue;

                var entityDict = vd.GetValue<ValuesDictionary>(i.ToString());
                if (!entityDict.ContainsKey("Overrides"))
                    continue;

                var overrides = entityDict.GetValue<ValuesDictionary>("Overrides");

                // 玩家组件在 "Player" 键下
                if (overrides.ContainsKey("Player"))
                {
                    var playerVD = overrides.GetValue<ValuesDictionary>("Player");

                    // 取回源实体（i-1 对应 entities 的索引）
                    var e = entities[i - 1];
                    var cp = e.FindComponent<ComponentPlayer>();
                    if (cp != null)
                    {
                        if (cp.PlayerGuid != to.PlayerGuid)
                        {
                            playerVD.SetValue("PlayerGuid", cp.PlayerData.Client.TokenId);
                        }
                    }
                }
            }
        }
    }
}
