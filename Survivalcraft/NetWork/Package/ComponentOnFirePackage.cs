namespace Game.NetWork.Packages;

public class ComponentOnFirePackage : IPackage {
    public byte ID => (byte)PackageType.OnFire;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public int m_attackerEntityId;
    public float m_duration;
    public int x, y, z;

    public enum EventType {
        ComponentOnFire,
        BlockOnFireAdd,
        BlockOnFireRemove
    }
    public EventType m_type;

    public ComponentOnFirePackage() {
    }
    public ComponentOnFirePackage(ComponentOnFire target, ComponentCreature attacker, float duration) {
        m_type = EventType.ComponentOnFire;
        m_duration = duration;
        if (attacker != null) m_attackerEntityId = attacker.Entity.Id;
        m_entityId = target.Entity.Id;
    }
    public ComponentOnFirePackage(int x, int y, int z, float expandability) {
        m_type = EventType.BlockOnFireAdd;
        this.x = x;
        this.y = y;
        this.z = z;
        m_duration = expandability;
    }
    public ComponentOnFirePackage(int x, int y, int z) {
        m_type = EventType.BlockOnFireRemove;
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.WriteEnum(m_type);
        switch (m_type) {
            case EventType.ComponentOnFire:
                writer.Write(m_entityId);
                writer.Write(m_attackerEntityId);
                writer.Write(m_duration);
                break;
            case EventType.BlockOnFireAdd:
                writer.Write(x);
                writer.Write(y);
                writer.Write(z);
                writer.Write(m_duration);
                break;
            case EventType.BlockOnFireRemove:
                writer.Write(x);
                writer.Write(y);
                writer.Write(z);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader) {
        m_type = reader.ReadEnum<EventType>();
        switch (m_type) {
            case EventType.ComponentOnFire:
                m_entityId = reader.ReadInt32();
                m_attackerEntityId = reader.ReadInt32();
                m_duration = reader.ReadSingle();
                break;
            case EventType.BlockOnFireAdd:
                x = reader.ReadInt32();
                y = reader.ReadInt32();
                z = reader.ReadInt32();
                m_duration = reader.ReadSingle();
                break;
            case EventType.BlockOnFireRemove:
                x = reader.ReadInt32();
                y = reader.ReadInt32();
                z = reader.ReadInt32();
                break;
        }
    }

    public void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        switch (m_type) {
            case EventType.BlockOnFireAdd:
                GameManager.Project.FindSubsystem<SubsystemFireBlockBehavior>().AddFireNet(x, y, z, m_duration);
                break;
            case EventType.BlockOnFireRemove:
                GameManager.Project.FindSubsystem<SubsystemFireBlockBehavior>().RemoveFireNet(x, y, z);
                break;
            case EventType.ComponentOnFire:
                GameManager.Project.FindEntity(m_entityId, (e) => {
                    ComponentOnFire onFire = e.FindComponent<ComponentOnFire>();
                    if (onFire != null) {
                        if (m_attackerEntityId == 0) onFire.SetOnFireNet(null, m_duration);
                        else {
                            GameManager.Project.FindEntity(m_attackerEntityId, (e2) => {
                                ComponentCreature creature = e2.FindComponent<ComponentCreature>();
                                onFire.SetOnFireNet(creature, m_duration);
                            });
                        }
                    }
                });
                break;
        }
    }
}
