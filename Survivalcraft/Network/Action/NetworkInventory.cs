using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkInventory {
        public static void OnSlotChanged(IInventory inventory, int slotIndex) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new InventorySyncPacket(inventory));
            }
        }
    }
}
