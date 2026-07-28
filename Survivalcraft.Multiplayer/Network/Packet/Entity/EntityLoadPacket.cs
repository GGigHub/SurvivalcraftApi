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
        entityDataList = entities.Count > 0
            ? entities[0].Project.SaveEntities(entities)
            : new EntityDataList { EntitiesData = [], Entities = [] };
    }

    public override void Serialize(PacketSerializer archive) {
        archive.EntityLoadList(ref entityDataList);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        List<Entity> el = GameManager.Project.InitializeAndLoadEntities(entityDataList);
        List<Entity> entitiesToRemove = [];
        foreach (Entity entity in el) {
            if (entity.Id == 0) {
                continue;
            }
            Entity existingEntity = GameManager.Project.FindEntity(entity.Id);
            if (existingEntity != null) {
                entitiesToRemove.Add(existingEntity);
            }
        }
        foreach (Entity entity in entitiesToRemove) {
            GameManager.Project.RemoveEntity(entity, true);
        }
        GameManager.Project.AttachEntities(el, true);
    }
}
