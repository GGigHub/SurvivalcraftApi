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
        GameManager.Project.AttachEntities(el, true);
    }
}

