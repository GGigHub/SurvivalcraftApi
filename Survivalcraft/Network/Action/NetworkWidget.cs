using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkWidget {
        public static void OnSleepRequest(ComponentSleep componentSleep, bool isSleep) {
            NetworkManager.QueuePacket(new SleepRequestPacket(componentSleep, isSleep));
        }

        public static void OnCrossbowPull(IInventory inventory, int slotIndex) {
            NetworkManager.QueuePacket(new CrossbowPullPacket(inventory, slotIndex));
        }

        public static void OnDispenserUpdate(Point3 position, byte data) {
            NetworkManager.QueuePacket(new DispenserPacket(position, data));
        }

        public static void OnMoveItem(IInventory sourceInventory, int sourceSlotIndex, IInventory targetInventory, int targetSlotIndex, int count) {
            NetworkManager.QueuePacket(new MoveItemPacket(sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count));
        }

        public static void OnDragDropItem(IInventory sourceInventory, int sourceSlotIndex, DragMode dragMode, IInventory targetInventory, int targetSlotIndex, bool processingOnly) {
            NetworkManager.QueuePacket(new DragDropItemPacket(sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly));
        }

        public static void OnPlayerDragDrop(PlayerData playerData, int inventoryId, int slotIndex, Vector3 worldPos, int count) {
            NetworkManager.QueuePacket(new PlayerDragDropPacket(playerData, inventoryId, slotIndex, worldPos, count));
        }

        public static void OnChokeRequest(ComponentHealth health, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability, InjureRequestPacket.RequestInjureType type) {
            NetworkManager.QueuePacket(new InjureRequestPacket(health, attacker, amount, cause, ignoreInvulnerability, type));
        }
    }
}
