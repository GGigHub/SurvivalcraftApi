using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerKnownPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerKnown;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerKnownPacket() { }

    public PlayerKnownPacket(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        // Typically server side would handle this to know client has loaded the player
    }
}

