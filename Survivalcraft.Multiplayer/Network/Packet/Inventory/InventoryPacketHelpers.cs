namespace Game.Network;

public static class InventoryPacketHelpers {
    public static bool IsSlotIndexValid(IInventory inventory, int index) {
        return inventory != null && index >= 0 && index < inventory.SlotsCount;
    }

    public static void QueueInventorySync(IInventory inventory, ClientSession to = null) {
        if (inventory == null) {
            return;
        }
        Packet packet = inventory is ComponentClothing clothing
            ? new ClothingSyncPacket(clothing)
            : new InventorySyncPacket(inventory);
        packet.To = to;
        NetworkManager.Queue(packet);
    }

    public static void QueueInventorySync(IInventory first, IInventory second, ClientSession to = null) {
        QueueInventorySync(first, to);
        if (second != first) {
            QueueInventorySync(second, to);
        }
    }
}
