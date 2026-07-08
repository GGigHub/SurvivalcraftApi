using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class EntityLoadPacket : Packet {
    public override byte ID => (byte)PacketType.EntityLoad;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public EntityDataList entityDataList;

    public EntityLoadPacket() {
    }

    public EntityLoadPacket(Entity entity) {
        entityDataList = entity.Project.SaveEntities([entity]);
    }

    public EntityLoadPacket(List<Entity> entities) {
        List<Entity> uniqueEntities = [];
        HashSet<Entity> seenEntities = [];
        foreach (Entity entity in entities) {
            if (entity != null && seenEntities.Add(entity)) {
                uniqueEntities.Add(entity);
            }
        }
        entityDataList = uniqueEntities.Count > 0
            ? uniqueEntities[0].Project.SaveEntities(uniqueEntities)
            : new EntityDataList { EntitiesData = [], Entities = [] };
    }

    public override void Serialize(PacketSerializer archive) {
        archive.EntityLoadList(ref entityDataList);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        List<Entity> el = GameManager.Project.InitializeAndLoadEntities(entityDataList);
        List<Entity> entitiesToAttach = [];
        HashSet<int> loadedIds = [];
        foreach (Entity entity in el) {
            if (entity.Id != 0) {
                if (!loadedIds.Add(entity.Id)) {
                    continue;
                }
                Entity existingEntity = GameManager.Project.FindEntity(entity.Id);
                if (existingEntity != null) {
                    if (existingEntity.FindComponent<ComponentPlayer>() != null) {
                        continue;
                    }
                    GameManager.Project.RemoveEntity(existingEntity, true);
                }
            }
            entitiesToAttach.Add(entity);
        }
        GameManager.Project.AttachEntities(entitiesToAttach, true);
    }
}
