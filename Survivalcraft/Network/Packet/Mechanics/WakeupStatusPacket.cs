using Engine;

namespace Game.Network;

public class WakeupStatusPacket : Packet {
    public override byte ID => (byte)PacketType.WakeupStatus;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;
    public int m_entityId;

    public WakeupStatusPacket() {
    }

    public WakeupStatusPacket(ComponentSleep sleep) {
        m_entityId = sleep.Entity.Id;
    }

    public override void Serialize(PacketSerializer archive) {
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

