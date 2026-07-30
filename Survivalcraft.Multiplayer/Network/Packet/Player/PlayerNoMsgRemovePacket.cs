using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerNoMsgRemovePacket : Packet {
    public override byte ID => (byte)PacketType.PlayerNoMsgRemove;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public override bool RequiresProject => true;
    public int m_playerIndex;

    public PlayerNoMsgRemovePacket() { }

    public PlayerNoMsgRemovePacket(int playerIndex) {
        m_playerIndex = playerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerIndex);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Remove(m_playerIndex.ToString());
    }
}

