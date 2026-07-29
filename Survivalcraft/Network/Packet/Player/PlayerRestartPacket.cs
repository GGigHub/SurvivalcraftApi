using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerRestartPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerRestart;
    public long m_requestId;
    public PlayerRestartPacket() { }
    public PlayerRestartPacket(PlayerData playerData) {
        m_fromPlayerIndex = playerData.PlayerIndex;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref m_requestId);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer
            && (playerData.ComponentPlayer == null
                || playerData.ComponentPlayer.ComponentHealth.Health > 0f)) {
            return;
        }
        if (isServer && !NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.PlayerRestart,
                m_requestId)) {
            return;
        }
        playerData.readyToRestart = true;
    }
}


