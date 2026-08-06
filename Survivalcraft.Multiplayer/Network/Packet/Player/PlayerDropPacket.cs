namespace Game.Network;

public class PlayerDropPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerDrop;

    public PlayerDropPacket() {
    }

    public PlayerDropPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
    }

    public override void Handle(bool isServer) {
        if (!isServer || !TryResolvePlayer(true, out PlayerData playerData)) {
            return;
        }
        playerData.ComponentPlayer.DoDrop();
    }
}