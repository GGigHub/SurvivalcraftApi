using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

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