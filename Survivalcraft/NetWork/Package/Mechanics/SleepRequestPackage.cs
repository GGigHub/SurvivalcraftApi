using Engine;

namespace Game.NetWork;

public class SleepRequestPackage : IPackage {
    public override byte ID => (byte)PackageType.SleepRequest;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public bool allowManualWakeup;

    public SleepRequestPackage() {
    }

    public SleepRequestPackage(ComponentSleep sleep, bool allow) {
        m_entityId = sleep.Entity.Id;
        allowManualWakeup = allow;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref allowManualWakeup);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentSleep sleep = e.FindComponent<ComponentSleep>();
            ComponentPlayer player = e.FindComponent<ComponentPlayer>();
            if (sleep != null && player != null && player.PlayerGuid == From.PlayerGuid) {
                if (sleep.CanSleep(out string reason)) {
                    sleep.Sleep(allowManualWakeup);
                }
                else {
                    NetworkManager.QueuePackage(new SleepStatusPackage(sleep, allowManualWakeup, false, reason) { To = From });
                }
            }
        });
    }
}