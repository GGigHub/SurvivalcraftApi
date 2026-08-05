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
