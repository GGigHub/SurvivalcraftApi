using Engine;

namespace Game.NetWork.Packages;

public class ComponentFluPackage : IPackage
{
    public byte ID => (byte)PackageType.ComponentFlu;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
    public enum EventType
    {
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

    public ushort entityId;

    public ComponentFluPackage()
    {

    }

    public ComponentFluPackage(ComponentFlu flu, EventType eventType)
    {
        entityId = flu.Entity.EntityId;
        m_eventType = eventType;
        m_fluOnset = flu.m_fluOnset;
        m_fluDuration = flu.m_fluDuration;
        m_coughDuration = flu.m_coughDuration;
        m_sneezeDuration = flu.m_sneezeDuration;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(entityId);
        writer.WriteEnum(m_eventType);
        writer.Write(m_fluOnset);
        writer.Write(m_fluDuration);
        writer.Write(m_coughDuration);
        writer.Write(m_sneezeDuration);
    }

    public void ReadData(PackageStreamReader reader)
    {
        entityId = reader.ReadUInt16();
        m_eventType = reader.ReadEnum<EventType>();
        m_fluOnset = reader.ReadSingle();
        m_fluDuration = reader.ReadSingle();
        m_coughDuration = reader.ReadSingle();
        m_sneezeDuration = reader.ReadSingle();
    }

        public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
        {
            if (isServer)
            {
                return;
            }
            switch (m_eventType)
            {
                case EventType.SyncStat:
                    projectNet.FindEntityById(entityId, (entity) => {
                        var flu = entity.FindComponent<ComponentFlu>();
                        if (flu != null)
                        {
                            flu.m_fluOnset = m_fluOnset;
                            flu.m_sneezeDuration = m_sneezeDuration;
                            flu.m_coughDuration = m_coughDuration;
                            flu.m_fluDuration = m_fluDuration;
                        }
                    });
                    break;
                case EventType.FluEffect:
                    projectNet.FindEntityById(entityId, (entity) => {
                        var flu = entity.FindComponent<ComponentFlu>();
                        if (flu != null)
                        {
                            flu.m_fluOnset = m_fluOnset;
                            flu.m_sneezeDuration = m_sneezeDuration;
                            flu.m_coughDuration = m_coughDuration;
                            flu.m_fluDuration = m_fluDuration;
                            flu.FluEffect();
                        }
                    });
                    break;
                case EventType.StartFlu:
                    projectNet.FindEntityById(entityId, (entity) => {
                        var flu = entity.FindComponent<ComponentFlu>();
                        if (flu != null)
                        {
                            flu.m_fluOnset = m_fluOnset;
                            flu.m_sneezeDuration = m_sneezeDuration;
                            flu.m_coughDuration = m_coughDuration;
                            flu.m_fluDuration = m_fluDuration;
                            flu.StartFlu();
                        }
                    });
                    break;
                case EventType.Sneeze:
                    projectNet.FindEntityById(entityId, (entity) => {
                        var flu = entity.FindComponent<ComponentFlu>();
                        if (flu != null)
                        {
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
