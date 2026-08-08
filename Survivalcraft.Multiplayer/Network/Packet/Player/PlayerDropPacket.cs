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

        ComponentPlayer player = playerData.ComponentPlayer;
        player.DoDrop();

        // ComponentCreativeInventory removes items without firing the normal
        // inventory-change hook, so the client would keep displaying the old
        // creative slot after a server-side Q drop.
        if (player.ComponentMiner?.Inventory is ComponentCreativeInventory inventory) {
            InventoryPacketHelpers.QueueInventorySync(inventory, From);
        }
    }
}
