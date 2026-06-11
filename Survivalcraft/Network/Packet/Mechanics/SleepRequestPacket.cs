using Engine;

namespace Game.Network;

public class SleepRequestPacket : Packet {
    public override byte ID => (byte)PacketType.SleepRequest;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;
    public bool allowManualWakeup;

    public SleepRequestPacket() {
    }

    public SleepRequestPacket(ComponentSleep sleep, bool allow) {
        m_entityId = sleep.Entity.Id;
        allowManualWakeup = allow;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref allowManualWakeup);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentSleep sleep = e.FindComponent<ComponentSleep>();
            ComponentPlayer player = e.FindComponent<ComponentPlayer>();
            if (sleep != null && player != null) {
                if (sleep.CanSleep(out string reason)) {
                    sleep.Sleep(allowManualWakeup);
                }
                else {
                    NetworkManager.Queue(new SleepStatusPacket(sleep, allowManualWakeup, false, reason) { To = From });
                }
            }
        });
    }
}

