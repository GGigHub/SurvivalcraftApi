using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkFurnace {
        public static void OnFurnaceUpdate(ComponentFurnace furnace) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel));
            }
        }
    }
}
