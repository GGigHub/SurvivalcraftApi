using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkInventory {
        public static void OnSlotChanged(IInventory inventory, int slotIndex) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new InventorySyncPacket(inventory));
            }
        }
    }
}
