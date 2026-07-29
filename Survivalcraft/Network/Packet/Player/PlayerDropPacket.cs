namespace Game.Network;

public class PlayerDropPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerDrop;
    public long m_requestId;

    public PlayerDropPacket() {
    }

    public PlayerDropPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (!isServer || !TryResolvePlayer(true, out PlayerData playerData)) {
            return;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.PlayerDrop,
                m_requestId)) {
            return;
        }

        playerData.ComponentPlayer.DoDrop();
    }
}
