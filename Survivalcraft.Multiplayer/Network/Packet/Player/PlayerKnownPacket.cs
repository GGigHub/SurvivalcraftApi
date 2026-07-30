using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerKnownPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerKnown;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public int m_playerIndex;

    public PlayerKnownPacket() { }

    public PlayerKnownPacket(int playerIndex) {
        m_playerIndex = playerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerIndex);
    }

    public override void Handle(bool isServer) {
        // Typically server side would handle this to know client has loaded the player
    }
}

