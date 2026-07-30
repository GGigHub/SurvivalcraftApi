

namespace Game.Network;

public class ComponentSicknessPacket : Packet {
    public override byte ID => (byte)PacketType.Sickness;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public enum EventType {
        SyncStat,
        NauseaEffect
    }

    public float m_sicknessDuration;
    public bool m_isNausea;
    public EventType m_eventType;
    public int entityId;

    public ComponentSicknessPacket() {
    }

    public ComponentSicknessPacket(ComponentSickness sickness, EventType eventType = EventType.SyncStat) {
        entityId = sickness.Entity.Id;
        m_eventType = eventType;
        m_sicknessDuration = sickness.m_sicknessDuration;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref entityId);
        archive.Enum(ref m_eventType);
        archive.Value(ref m_sicknessDuration);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(entityId, entity => {
            var sickness = entity.FindComponent<ComponentSickness>();
            if (sickness != null) {
                sickness.m_sicknessDuration = m_sicknessDuration;
                if (m_eventType == EventType.NauseaEffect) {
                    sickness.NauseaEffect(true);
                }
            }
        });
    }
}

