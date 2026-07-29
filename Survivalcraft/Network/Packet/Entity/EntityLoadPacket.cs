using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public struct EntityLifecycleStamp {
    public int EntityId;
    public long Revision;
}

public class EntityLoadPacket : Packet {
    public override byte ID => (byte)PacketType.EntityLoad;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public EntityDataList entityDataList;
    public List<EntityLifecycleStamp> lifecycleStamps = [];

    public EntityLoadPacket() {
    }

    public EntityLoadPacket(Entity entity) {
        entityDataList = entity.Project.SaveEntities([entity]);
        lifecycleStamps = CreateLifecycleStamps([entity]);
    }

    public EntityLoadPacket(List<Entity> entities) {
        entityDataList = entities.Count > 0
            ? entities[0].Project.SaveEntities(entities)
            : new EntityDataList { EntitiesData = [], Entities = [] };
        lifecycleStamps = CreateLifecycleStamps(entities);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.EntityLoadList(ref entityDataList);
        SerializeLifecycleStamps(archive, ref lifecycleStamps);
    }

    internal static void SerializeLifecycleStamps(
        PacketSerializer archive,
        ref List<EntityLifecycleStamp> stamps) {
        int count = archive.IsWriting ? stamps.Count : 0;
        archive.Value(ref count);
        if (count is < 0 or > 65_536) {
            throw new InvalidDataException($"Invalid entity lifecycle stamp count {count}.");
        }
        if (archive.IsReading) {
            stamps = new List<EntityLifecycleStamp>(count);
            for (int i = 0; i < count; i++) {
                EntityLifecycleStamp stamp = new();
                archive.Value(ref stamp.EntityId);
                archive.Value(ref stamp.Revision);
                stamps.Add(stamp);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                EntityLifecycleStamp stamp = stamps[i];
                archive.Value(ref stamp.EntityId);
                archive.Value(ref stamp.Revision);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        Project project = GameManager.Project;
        List<Entity> loadedEntities = project.InitializeAndLoadEntities(entityDataList);
        Dictionary<int, long> revisions = [];
        foreach (EntityLifecycleStamp stamp in lifecycleStamps) {
            if (stamp.EntityId > 0) {
                revisions.TryAdd(stamp.EntityId, stamp.Revision);
            }
        }
        List<Entity> acceptedEntities = [];
        List<Entity> entitiesToRemove = [];
        HashSet<int> acceptedIds = [];
        foreach (Entity entity in loadedEntities) {
            if (entity.Id <= 0
                || !revisions.TryGetValue(entity.Id, out long revision)
                || !acceptedIds.Add(entity.Id)) {
                entity.Dispose();
                continue;
            }
            Entity existingEntity = project.FindEntity(entity.Id);
            EntityLifecycleDecision decision = EntityLifecycleTracker.ReceiveLoad(
                project,
                entity.Id,
                revision,
                forceRefresh: existingEntity == null);
            if (!decision.Apply) {
                entity.Dispose();
                continue;
            }
            if (existingEntity != null) {
                entitiesToRemove.Add(existingEntity);
            }
            acceptedEntities.Add(entity);
        }
        foreach (Entity entity in entitiesToRemove) {
            project.RemoveEntity(entity, true);
        }
        if (acceptedEntities.Count > 0) {
            project.AttachEntities(acceptedEntities, true);
        }
    }

    private static List<EntityLifecycleStamp> CreateLifecycleStamps(
        IEnumerable<Entity> entities) {
        List<EntityLifecycleStamp> result = [];
        foreach (Entity entity in entities) {
            if (entity.Id <= 0) {
                continue;
            }
            result.Add(new EntityLifecycleStamp {
                EntityId = entity.Id,
                Revision = EntityLifecycleTracker.GetAuthoritativeRevision(
                    entity.Project,
                    entity.Id)
            });
        }
        return result;
    }
}
