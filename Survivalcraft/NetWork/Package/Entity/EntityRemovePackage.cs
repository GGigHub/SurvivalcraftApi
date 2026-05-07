using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

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