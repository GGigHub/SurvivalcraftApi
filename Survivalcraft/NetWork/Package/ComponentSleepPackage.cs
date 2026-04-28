using Engine;

namespace Game.NetWork.Packages;

public class ComponentSleepPackage : IPackage {
    public override byte ID => (byte)PackageType.Sleep;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public string reason = "";

    public enum EventType {
        SleepRequest,
        Sleep,
        WakeupRequest,
        WakeUp
    }

    public EventType m_type;
    public bool m_result;
    public bool allowManualWakeup;

    public ComponentSleepPackage() {
    }

    public ComponentSleepPackage(ComponentSleep sleep, EventType eventType, bool allow = false, bool result = false, string msg = "") {
        reason = msg;
        m_entityId = sleep.Entity.Id;
        m_type = eventType;
        allowManualWakeup = allow;
        m_result = result;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Enum(ref m_type);
        archive.Value(ref allowManualWakeup);
        archive.Value(ref m_result);
        archive.Value(ref reason);
    }

    public override void Handle(bool isServer) {
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentSleep sleep = e.FindComponent<ComponentSleep>();
            if (sleep == null) {
                return;
            }
            ComponentPlayer player = e.FindComponent<ComponentPlayer>();
            if (player == null) {
                return;
            }
            if (isServer && player.PlayerGuid != From.PlayerGuid) {
                return;
            }
            switch (m_type) {
                case EventType.SleepRequest:
                    if (sleep.CanSleep(out string reason2)) {
                        sleep.Sleep(allowManualWakeup);
                    }
                    else {
                        NetworkManager.QueuePackage(new ComponentSleepPackage(sleep, EventType.Sleep, allowManualWakeup, false, reason2) { To = From });
                    }
                    break;
                case EventType.Sleep:
                    if (m_result) {
                        sleep.NetSleep(allowManualWakeup);
                    }
                    else {
                        var player1 = sleep.Entity.FindComponent<ComponentPlayer>();
                        player1.ComponentGui.DisplaySmallMessage(reason, Color.White, false, true);
                    }
                    break;
                case EventType.WakeupRequest:
                    sleep.WakeUp();
                    break;
                case EventType.WakeUp:
                    sleep.NetWakeUp();
                    break;
            }
        });
    }
}
