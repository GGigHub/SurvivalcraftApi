using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class InventoryPacketHelpers {
    public const float MaximumContainerAccessDistance = 6f;

    public static bool IsSlotIndexValid(IInventory inventory, int index) {
        return inventory != null && index >= 0 && index < inventory.SlotsCount;
    }

    public static bool IsActiveSlotIndexValid(IInventory inventory, int index) {
        return inventory != null
            && index >= 0
            && index < inventory.SlotsCount
            && index < inventory.VisibleSlotsCount;
    }

    public static bool IsOwnedBySession(IInventory inventory, ClientSession from) {
        if (inventory is not Component component || from == null) {
            return false;
        }
        ComponentPlayer owner = component.Entity?.FindComponent<ComponentPlayer>();
        return owner?.PlayerData != null
            && owner.PlayerData.PlayerIndex == from.PlayerIndex;
    }

    public static bool CanSessionAccessInventory(IInventory inventory, ClientSession from) {
        if (inventory == null || from == null) {
            return false;
        }
        ComponentPlayer actor = from.GetPlayerData()?.ComponentPlayer;
        if (actor?.Entity?.Project != inventory.Project) {
            return false;
        }
        if (inventory is not Component component || component.Entity == null) {
            return false;
        }

        ComponentPlayer owner = component.Entity.FindComponent<ComponentPlayer>();
        if (owner?.PlayerData != null) {
            return owner.PlayerData.PlayerIndex == from.PlayerIndex;
        }

        Vector3 target;
        ComponentBlockEntity blockEntity = component.Entity.FindComponent<ComponentBlockEntity>();
        if (blockEntity != null) {
            target = new Vector3(blockEntity.Coordinates) + new Vector3(0.5f);
        }
        else {
            ComponentBody body = component.Entity.FindComponent<ComponentBody>();
            if (body == null) {
                return false;
            }
            target = body.BoundingBox.Center();
        }
        return Vector3.DistanceSquared(actor.ComponentBody.Position, target)
            <= MaximumContainerAccessDistance * MaximumContainerAccessDistance;
    }

    public static long GetLifecycleRevision(IInventory inventory) {
        if (inventory is Component component && component.Entity?.Id > 0) {
            return EntityLifecycleTracker.GetAuthoritativeRevision(
                component.Entity.Project,
                component.Entity.Id);
        }
        return -1;
    }

    public static void ApplyAuthoritativeActiveSlot(IInventory inventory, int index) {
        if (!IsActiveSlotIndexValid(inventory, index)) {
            return;
        }
        inventory.ActiveSlotIndex = index;
        if (inventory is Component component) {
            ComponentPlayer player = component.Entity?.FindComponent<ComponentPlayer>();
            if (player != null) {
                player.LastActiveSlot = (byte)inventory.ActiveSlotIndex;
            }
        }
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
