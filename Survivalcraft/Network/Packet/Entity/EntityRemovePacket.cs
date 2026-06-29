using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class EntityRemovePacket : Packet {
    public override byte ID => (byte)PacketType.EntityRemove;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;

    public EntityRemovePacket() {
    }

    public EntityRemovePacket(int id) {
        m_entityId = id;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        List<Entity> entitiesToRemove = [];
        foreach (Entity entity in GameManager.Project.Entities) {
            if (entity.Id == m_entityId) {
                entitiesToRemove.Add(entity);
            }
        }
        foreach (Entity entity in entitiesToRemove) {
            GameManager.Project.RemoveEntity(entity, true);
        }
    }
}

