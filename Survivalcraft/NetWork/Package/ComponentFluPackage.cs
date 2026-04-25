namespace Game.NetWork.Packages;

public class ComponentFluPackage : IPackage {
    public byte ID => (byte)PackageType.Flu;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;
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

    public ComponentFluPackage() {

    }

    public ComponentFluPackage(ComponentFlu flu, EventType eventType) {
        entityId = flu.Entity.Id;
        m_eventType = eventType;
        m_fluOnset = flu.m_fluOnset;
        m_fluDuration = flu.m_fluDuration;
        m_coughDuration = flu.m_coughDuration;
        m_sneezeDuration = flu.m_sneezeDuration;
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.Write(entityId);
        writer.WriteEnum(m_eventType);
        writer.Write(m_fluOnset);
        writer.Write(m_fluDuration);
        writer.Write(m_coughDuration);
        writer.Write(m_sneezeDuration);
    }

    public void ReadData(PackageStreamReader reader) {
        entityId = reader.ReadInt32();
        m_eventType = reader.ReadEnum<EventType>();
        m_fluOnset = reader.ReadSingle();
        m_fluDuration = reader.ReadSingle();
        m_coughDuration = reader.ReadSingle();
        m_sneezeDuration = reader.ReadSingle();
    }

    public void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        switch (m_eventType) {
            case EventType.SyncStat:
                GameManager.Project.FindEntity(entityId, (entity) => {
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
                GameManager.Project.FindEntity(entityId, (entity) => {
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
                GameManager.Project.FindEntity(entityId, (entity) => {
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
                GameManager.Project.FindEntity(entityId, (entity) => {
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
