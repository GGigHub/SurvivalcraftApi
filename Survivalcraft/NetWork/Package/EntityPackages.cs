using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork.Packages;

public class EntityRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.EntityRemove;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;

    public EntityRemovePackage() {
    }

    public EntityRemovePackage(int id) {
        m_entityId = id;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_entityId, entity => {
            GameManager.Project.RemoveEntity(entity, true);
        });
    }
}

public class EntityLoadPackage : IPackage {
    public override byte ID => (byte)PackageType.EntityLoad;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public EntityDataList entityDataList;

    public EntityLoadPackage() {
    }

    public EntityLoadPackage(Entity entity) {
        entityDataList = GameManager.Project.SaveEntities([entity]);
    }

    public EntityLoadPackage(List<Entity> entities) {
        entityDataList = GameManager.Project.SaveEntities([.. entities]);
    }

    public override void Serialize(PackageSerializer archive) {
        archive.EntityLoadList(ref entityDataList);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        List<Entity> el = GameManager.Project.InitializeAndLoadEntities(entityDataList);
        GameManager.Project.AttachEntities(el, true);
    }
}

public class EntitySyncRequestPackage : IPackage {
    public override byte ID => (byte)PackageType.EntitySyncRequest;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<int> m_entityIdList;

    public EntitySyncRequestPackage() {
    }

    public EntitySyncRequestPackage(List<int> idList) {
        m_entityIdList = [.. idList];
    }

    public override void Serialize(PackageSerializer archive) {
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
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        List<Entity> el = new List<Entity>();
        foreach (int e in m_entityIdList) {
            GameManager.Project.FindEntity(e, entity => el.Add(entity));
        }
        NetworkManager.QueuePackage(new EntityLoadPackage(el) { To = From });
    }
}
