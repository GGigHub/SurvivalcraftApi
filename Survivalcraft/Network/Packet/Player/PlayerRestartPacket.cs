using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerRestartPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerRestart;
    public PlayerRestartPacket() { }
    public PlayerRestartPacket(PlayerData playerData) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerId) : null);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.readyToRestart = true;
    }
}

