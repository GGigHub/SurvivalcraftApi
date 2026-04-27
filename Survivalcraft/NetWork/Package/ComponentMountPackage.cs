namespace Game.NetWork.Packages;

public class ComponentMountPackage : IPackage {
    public override byte ID => (byte)PackageType.Mount;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public int m_fromId;

    public EventType m_type;

    public enum EventType {
        Mount,
        Dismount,
        MountRequest,
        DismountRequest
    }

    public ComponentMountPackage() {
    }

    public ComponentMountPackage(ComponentRider rider, ComponentMount mount, bool isRequest = false) {
        m_type = isRequest ? EventType.MountRequest : EventType.Mount;
        m_fromId = rider.Entity.Id;
        m_targetId = mount.Entity.Id;
    }

    public ComponentMountPackage(ComponentRider rider, bool isRequest = false) {
        m_type = isRequest ? EventType.DismountRequest : EventType.Dismount;
        m_fromId = rider.Entity.Id;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        archive.Value(ref m_fromId);
        archive.Value(ref m_targetId);
    }

    public override void Handle(bool isServer) {
        if (isServer && (m_type != EventType.MountRequest && m_type != EventType.DismountRequest)) {
            return;
        }
        switch (m_type) {
            case EventType.Dismount:
                GameManager.Project.FindEntity(m_fromId, entity => {
                    var rider = entity.FindComponent<ComponentRider>();
                    rider?.StartNetDismounting();
                    if (!isServer && rider.Mount != null) {
                        var select = rider.Mount.Entity.FindComponent<ComponentBehaviorSelector>();
                        if (select != null) {
                            select.IsDisableBehavior = true;
                        }
                    }
                });
                break;
            case EventType.DismountRequest:
                GameManager.Project.FindEntity(m_fromId, entity => {
                    var rider = entity.FindComponent<ComponentRider>();
                    rider?.StartDismounting();
                });
                break;
            case EventType.Mount:
                GameManager.Project.FindEntity(m_fromId, entity => {
                    var rider = entity.FindComponent<ComponentRider>();
                    GameManager.Project.FindEntity(m_targetId, entity2 => {
                        var mount = entity2.FindComponent<ComponentMount>();
                        if (mount != null && rider != null) {
                            rider.StartNetMounting(mount);
                            if (!isServer) {
                                var select = rider.Mount.Entity.FindComponent<ComponentBehaviorSelector>();
                                if (select != null) {
                                    select.IsDisableBehavior = false;
                                }
                            }
                        }
                    });
                });
                break;
            case EventType.MountRequest:
                GameManager.Project.FindEntity(m_fromId, entity => {
                    var rider = entity.FindComponent<ComponentRider>();
                    GameManager.Project.FindEntity(m_targetId, entity2 => {
                        var mount = entity2.FindComponent<ComponentMount>();
                        if (mount != null && rider != null) {
                            rider.StartMounting(mount);
                        }
                    });
                });
                break;
        }
    }
}
