using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerIntoPlayingPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerIntoPlaying;
    public PlayerIntoPlayingPacket() { }
    public PlayerIntoPlayingPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
    }
}


