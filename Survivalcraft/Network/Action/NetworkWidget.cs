using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkWidget {
        public static void OnSleepRequest(ComponentSleep componentSleep, bool isSleep) {
            NetworkService.QueuePacket(new SleepRequestPacket(componentSleep, isSleep));
        }

        public static void OnCrossbowPull(IInventory inventory, int slotIndex) {
            NetworkService.QueuePacket(new CrossbowPullPacket(inventory, slotIndex));
        }

        public static void OnDispenserUpdate(Point3 position, byte data) {
            NetworkService.QueuePacket(new DispenserPacket(position, data));
        }

        public static void OnMoveItem(IInventory sourceInventory, int sourceSlotIndex, IInventory targetInventory, int targetSlotIndex, int count) {
            NetworkService.QueuePacket(new MoveItemPacket(sourceInventory, sourceSlotIndex, targetInventory, targetSlotIndex, count));
        }

        public static void OnDragDropItem(IInventory sourceInventory, int sourceSlotIndex, DragMode dragMode, IInventory targetInventory, int targetSlotIndex, bool processingOnly) {
            NetworkService.QueuePacket(new DragDropItemPacket(sourceInventory, sourceSlotIndex, dragMode, targetInventory, targetSlotIndex, processingOnly));
        }

        public static void OnPlayerDragDrop(PlayerData playerData, int inventoryId, int slotIndex, Vector3 worldPos, int count) {
            NetworkService.QueuePacket(new PlayerDragDropPacket(playerData, inventoryId, slotIndex, worldPos, count));
        }

        public static void OnChokeRequest(ComponentHealth health, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability) { var type = InjureRequestPacket.RequestInjureType.Choke;
            NetworkService.QueuePacket(new InjureRequestPacket(health, attacker, amount, cause, ignoreInvulnerability, type));
        }
    }
}
