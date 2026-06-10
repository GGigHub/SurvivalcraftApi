using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerNoMsgAddPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerNoMsgAdd;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public int m_playerIndex;

    public PlayerNoMsgAddPacket() { }

    public PlayerNoMsgAddPacket(int playerIndex) {
        m_playerIndex = playerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerIndex);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Add(m_playerIndex.ToString());
    }
}

