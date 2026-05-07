using Engine;

namespace Game.NetWork;

public class WakeupRequestPackage : IPackage {
    public override byte ID => (byte)PackageType.WakeupRequest;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;

    public WakeupRequestPackage() {
    }

    public WakeupRequestPackage(ComponentSleep sleep) {
        m_entityId = sleep.Entity.Id;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentSleep sleep = e.FindComponent<ComponentSleep>();
            ComponentPlayer player = e.FindComponent<ComponentPlayer>();
            if (sleep != null && player != null && player.PlayerGuid == From.PlayerGuid) {
                sleep.WakeUp();
            }
        });
    }
}