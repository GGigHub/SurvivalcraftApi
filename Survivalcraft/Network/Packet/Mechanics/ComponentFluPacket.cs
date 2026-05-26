

namespace Game.Network;

public class ComponentFluPacket : Packet {
    public override byte ID => (byte)PacketType.Flu;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public enum EventType {
        SyncStat,
        FluEffect,
        StartFlu,
        Sneeze
    }

    public float m_fluDuration;
    public float m_coughDuration;
    public float m_sneezeDuration;
    public float m_fluOnset;
    public Engine.Vector3 m_Impulse;

    public EventType m_eventType;

    public int entityId;

    public ComponentFluPacket() {
    }

    public ComponentFluPacket(ComponentFlu flu, EventType eventType) {
        entityId = flu.Entity.Id;
        m_eventType = eventType;
        m_fluOnset = flu.m_fluOnset;
        m_fluDuration = flu.m_fluDuration;
        m_coughDuration = flu.m_coughDuration;
        m_sneezeDuration = flu.m_sneezeDuration;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref entityId);
        archive.Enum(ref m_eventType);
        archive.Value(ref m_fluOnset);
        archive.Value(ref m_fluDuration);
        archive.Value(ref m_coughDuration);
        archive.Value(ref m_sneezeDuration);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        switch (m_eventType) {
            case EventType.SyncStat:
                GameManager.Project.FindEntity(entityId, entity => {
                    var flu = entity.FindComponent<ComponentFlu>();
                    if (flu != null) {
                        flu.m_fluOnset = m_fluOnset;
                        flu.m_sneezeDuration = m_sneezeDuration;
                        flu.m_coughDuration = m_coughDuration;
                        flu.m_fluDuration = m_fluDuration;
                    }
                });
                break;
            case EventType.FluEffect:
                GameManager.Project.FindEntity(entityId, entity => {
                    var flu = entity.FindComponent<ComponentFlu>();
                    if (flu != null) {
                        flu.m_fluOnset = m_fluOnset;
                        flu.m_sneezeDuration = m_sneezeDuration;
                        flu.m_coughDuration = m_coughDuration;
                        flu.m_fluDuration = m_fluDuration;
                        flu.FluEffect();
                    }
                });
                break;
            case EventType.StartFlu:
                GameManager.Project.FindEntity(entityId, entity => {
                    var flu = entity.FindComponent<ComponentFlu>();
                    if (flu != null) {
                        flu.m_fluOnset = m_fluOnset;
                        flu.m_sneezeDuration = m_sneezeDuration;
                        flu.m_coughDuration = m_coughDuration;
                        flu.m_fluDuration = m_fluDuration;
                        flu.StartFlu();
                    }
                });
                break;
            case EventType.Sneeze:
                GameManager.Project.FindEntity(entityId, entity => {
                    var flu = entity.FindComponent<ComponentFlu>();
                    if (flu != null) {
                        flu.m_fluOnset = m_fluOnset;
                        flu.m_sneezeDuration = m_sneezeDuration;
                        flu.m_coughDuration = m_coughDuration;
                        flu.m_fluDuration = m_fluDuration;
                        flu.Sneeze();
                    }
                });
                break;
        }
    }
}

