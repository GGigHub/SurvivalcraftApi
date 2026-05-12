using Engine;

namespace Game.Network;

public class WakeupRequestPacket : Packet {
    public override byte ID => (byte)PacketType.WakeupRequest;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;
    public int m_entityId;

    public WakeupRequestPacket() {
    }

    public WakeupRequestPacket(ComponentSleep sleep) {
        m_entityId = sleep.Entity.Id;
    }

    public override void Serialize(PacketSerializer archive) {
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

