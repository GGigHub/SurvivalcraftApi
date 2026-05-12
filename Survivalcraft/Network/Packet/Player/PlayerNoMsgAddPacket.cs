using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerNoMsgAddPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerNoMsgAdd;
    public override ClientState MinNeedState => ClientState.Connected;
    public Guid m_playerGuid;

    public PlayerNoMsgAddPacket() { }

    public PlayerNoMsgAddPacket(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Add(m_playerGuid.ToString());
    }
}

