using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerIntoPlayingPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerIntoPlaying;
    public PlayerIntoPlayingPacket() { }
    public PlayerIntoPlayingPacket(ComponentPlayer player) {
        m_fromPlayerId = NetworkService.GetClientByGUID(player.PlayerData.NetworkGuid).ID;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerId) : null);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
    }
}

