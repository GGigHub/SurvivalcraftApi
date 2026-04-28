namespace Game.NetWork.Packages;

public class ComponentOnFirePackage : IPackage {
    public override byte ID => (byte)PackageType.OnFire;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public int m_attackerEntityId;
    public float m_duration;
    public int x;
    public int y;
    public int z;

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
        if (attacker != null) {
            m_attackerEntityId = attacker.Entity.Id;
        }
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

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        switch (m_type) {
            case EventType.ComponentOnFire:
                archive.Value(ref m_entityId);
                archive.Value(ref m_attackerEntityId);
                archive.Value(ref m_duration);
                break;
            case EventType.BlockOnFireAdd:
                archive.Value(ref x);
                archive.Value(ref y);
                archive.Value(ref z);
                archive.Value(ref m_duration);
                break;
            case EventType.BlockOnFireRemove:
                archive.Value(ref x);
                archive.Value(ref y);
                archive.Value(ref z);
                break;
        }
    }

    public override void Handle(bool isServer) {
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
                GameManager.Project.FindEntity(m_entityId, e => {
                    ComponentOnFire onFire = e.FindComponent<ComponentOnFire>();
                    if (onFire != null) {
                        if (m_attackerEntityId == 0) {
                            onFire.SetOnFireNet(null, m_duration);
                        }
                        else {
                            GameManager.Project.FindEntity(m_attackerEntityId, e2 => {
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
