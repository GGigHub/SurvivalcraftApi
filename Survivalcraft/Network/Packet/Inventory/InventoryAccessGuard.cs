using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class InventoryAccessGuard {
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

