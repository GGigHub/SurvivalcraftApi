using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class EntityRemovePacket : Packet {
    public override byte ID => (byte)PacketType.EntityRemove;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
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
        GameManager.Project.FindEntity(m_entityId, entity => {
            GameManager.Project.RemoveEntity(entity, true);
        });
    }
}

