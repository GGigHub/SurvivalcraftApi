using Engine;

namespace Game.NetWork;

public class WakeupStatusPackage : IPackage {
    public override byte ID => (byte)PackageType.WakeupStatus;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;

    public WakeupStatusPackage() {
    }

    public WakeupStatusPackage(ComponentSleep sleep) {
        m_entityId = sleep.Entity.Id;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentSleep sleep = e.FindComponent<ComponentSleep>();
            sleep?.NetWakeUp();
        });
    }
}