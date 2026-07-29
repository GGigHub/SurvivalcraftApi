using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class EntityRemovePacket : Packet {
    public override byte ID => (byte)PacketType.EntityRemove;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;
    public long m_revision;

    public EntityRemovePacket() {
    }

    public EntityRemovePacket(int id, long revision) {
        m_entityId = id;
        m_revision = revision;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (isServer || m_entityId <= 0 || m_revision < 0) return;
        Project project = GameManager.Project;
        Entity entity = project.FindEntity(m_entityId);
        EntityLifecycleDecision decision = EntityLifecycleTracker.ReceiveRemoval(
            project,
            m_entityId,
            m_revision,
            forceRefresh: entity != null && !EntityLifecycleTracker.IsPresent(project, m_entityId));
        if (decision.Apply && entity != null) {
            project.RemoveEntity(entity, true);
        }
    }
}

