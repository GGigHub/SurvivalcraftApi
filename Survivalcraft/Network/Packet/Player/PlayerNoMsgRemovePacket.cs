using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerNoMsgRemovePacket : Packet {
    public override byte ID => (byte)PacketType.PlayerNoMsgRemove;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerNoMsgRemovePacket() { }

    public PlayerNoMsgRemovePacket(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Remove(m_playerGuid.ToString());
    }
}

