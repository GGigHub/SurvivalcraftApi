using Engine;

namespace Game.NetWork.Packages;

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
