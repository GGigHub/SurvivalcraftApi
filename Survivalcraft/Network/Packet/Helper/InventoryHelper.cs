using Engine;
using GameEntitySystem;

namespace Game.Network;

public class InventorySlot {
    public int inventoryId;
    public int slotIndex;
    public int value;
    public int count;
}


public static class InventoryHelper {
    public static bool IsOwnedByOtherPlayer(IInventory inventory, Client from, out ComponentPlayer owner) {
        return InventoryAccessHelper.IsOwnedByOtherPlayer(inventory, from, out owner);
    }

    public static bool IsSlotIndexValid(IInventory inventory, int index) {
        return InventoryAccessHelper.IsSlotIndexValid(inventory, index);
    }
}


public static class InventoryAccessHelper {
    public static bool IsOwnedByOtherPlayer(IInventory inventory, Client from, out ComponentPlayer owner) {
        owner = null;
        if (inventory is Component component) {
            owner = component.Entity?.FindComponent<ComponentPlayer>();
            if (owner != null && from != null) {
                return owner.PlayerGuid != from.PlayerGuid;
            }
        }
        return false;
    }

    public static bool IsSlotIndexValid(IInventory inventory, int index) {
        return inventory != null && index >= 0 && index < inventory.SlotsCount;
    }
}

