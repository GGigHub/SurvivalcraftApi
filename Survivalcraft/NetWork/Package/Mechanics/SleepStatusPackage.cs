using Engine;

namespace Game.NetWork;

public class SleepStatusPackage : IPackage {
    public override byte ID => (byte)PackageType.SleepStatus;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public bool allowManualWakeup;
    public bool m_result;
    public string reason = "";

    public SleepStatusPackage() {
    }

    public SleepStatusPackage(ComponentSleep sleep, bool allow, bool result, string msg = "") {
        m_entityId = sleep.Entity.Id;
        allowManualWakeup = allow;
        m_result = result;
        reason = msg;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref allowManualWakeup);
        archive.Value(ref m_result);
        archive.Value(ref reason);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentSleep sleep = e.FindComponent<ComponentSleep>();
            if (sleep != null) {
                if (m_result) {
                    sleep.NetSleep(allowManualWakeup);
                }
                else {
                    var player = sleep.Entity.FindComponent<ComponentPlayer>();
                    player?.ComponentGui.DisplaySmallMessage(reason, Color.White, false, true);
                }
            }
        });
    }
}