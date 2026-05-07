using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

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