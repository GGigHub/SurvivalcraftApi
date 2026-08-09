using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class EntitySyncRequestPacket : Packet {
    public const int MaximumEntityCount = 1024;

    public override byte ID => (byte)PacketType.EntitySyncRequest;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<int> m_entityIdList;

    public EntitySyncRequestPacket() {
    }

    public EntitySyncRequestPacket(List<int> idList) {
        m_entityIdList = [.. idList];
    }

    public override void Serialize(PacketSerializer archive) {
        int count = archive.IsWriting ? m_entityIdList.Count : 0;
        archive.Value(ref count);
        if (count is < 0 or > MaximumEntityCount) {
            throw new InvalidDataException(
                $"Invalid entity sync request count {count}; maximum is {MaximumEntityCount}.");
        }
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
        if (!isServer || From == null || m_entityIdList == null) return;
        List<Entity> el = new List<Entity>();
        HashSet<int> seenEntityIds = [];
        foreach (int e in m_entityIdList) {
            if (e > 0 && seenEntityIds.Add(e)) {
                GameManager.Project.FindEntity(e, entity => el.Add(entity));
            }
        }
        NetworkManager.Queue(new EntityLoadPacket(el) { To = From });
        foreach (Entity entity in el) {
            NetworkEntityStateBaselineFactory.QueueForSession(entity, From);
        }
    }
}

