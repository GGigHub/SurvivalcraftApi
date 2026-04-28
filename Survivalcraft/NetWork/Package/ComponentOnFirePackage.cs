namespace Game.NetWork.Packages;

public class EntityOnFirePackage : IPackage {
    public override byte ID => (byte)PackageType.EntityOnFire;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public int m_attackerEntityId;
    public float m_duration;

    public EntityOnFirePackage() { }
    public EntityOnFirePackage(ComponentOnFire target, ComponentCreature attacker, float duration) {
        m_duration = duration;
        if (attacker != null) {
            m_attackerEntityId = attacker.Entity.Id;
        }
        m_entityId = target.Entity.Id;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_attackerEntityId);
        archive.Value(ref m_duration);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentOnFire onFire = e.FindComponent<ComponentOnFire>();
            if (onFire == null) {
                return;
            }
            if (m_attackerEntityId == 0) {
                onFire.SetOnFireNet(null, m_duration);
            }
            else {
                GameManager.Project.FindEntity(m_attackerEntityId, e2 => {
                    ComponentCreature creature = e2.FindComponent<ComponentCreature>();
                    onFire.SetOnFireNet(creature, m_duration);
                });
            }
        });
    }
}

public class FireBlockAddPackage : IPackage {
    public override byte ID => (byte)PackageType.FireBlockAdd;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int x;
    public int y;
    public int z;
    public float m_duration;

    public FireBlockAddPackage() { }
    public FireBlockAddPackage(int x, int y, int z, float expandability) {
        this.x = x;
        this.y = y;
        this.z = z;
        m_duration = expandability;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref x);
        archive.Value(ref y);
        archive.Value(ref z);
        archive.Value(ref m_duration);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemFireBlockBehavior>().AddFireNet(x, y, z, m_duration);
    }
}

public class FireBlockRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.FireBlockRemove;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int x;
    public int y;
    public int z;

    public FireBlockRemovePackage() { }
    public FireBlockRemovePackage(int x, int y, int z) {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref x);
        archive.Value(ref y);
        archive.Value(ref z);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemFireBlockBehavior>().RemoveFireNet(x, y, z);
    }
}
