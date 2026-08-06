namespace Game.Network;

public static class InventoryPacketHelpers {
    private static readonly HashSet<IInventory> s_pendingInventories = [];

    public static bool IsSlotIndexValid(IInventory inventory, int index) {
        return inventory != null && index >= 0 && index < inventory.SlotsCount;
    }

    public static void MarkInventoryDirty(IInventory inventory) {
        if (inventory != null && NetworkManager.IsServerRunning) {
            s_pendingInventories.Add(inventory);
        }
    }

    public static void FlushPendingSync() {
        if (s_pendingInventories.Count == 0) {
            return;
        }

        IInventory[] inventories = [.. s_pendingInventories];
        s_pendingInventories.Clear();
        if (!NetworkManager.IsServerRunning) {
            return;
        }

        foreach (IInventory inventory in inventories) {
            QueueInventorySync(inventory);
        }
    }

    public static void ClearPendingSync() {
        s_pendingInventories.Clear();
    }

    public static bool ApplySlotValue(IInventory inventory, int slotIndex, int value, int count) {
        if (!IsSlotIndexValid(inventory, slotIndex)) {
            return false;
        }

        // A slot with no items must not retain a stale value. This also keeps
        // malformed correction packets from producing an invalid slot state.
        if (value == 0 || count <= 0) {
            value = 0;
            count = 0;
        }

        if (inventory.GetSlotValue(slotIndex) == value
            && inventory.GetSlotCount(slotIndex) == count) {
            return false;
        }

        // SetSlotValue updates the complete slot in one operation. In
        // particular, do not implement this as RemoveSlotItems + AddSlotItems:
        // the latter exposes a transient empty slot to the UI and fires two
        // slot-change notifications.
        inventory.SetSlotValue(slotIndex, new ComponentInventoryBase.Slot {
            Value = value,
            Count = count
        });
        return true;
    }

    public static void QueueInventorySync(IInventory inventory, ClientSession to = null) {
        if (inventory == null) {
            return;
        }
        if (to == null) {
            s_pendingInventories.Remove(inventory);
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
