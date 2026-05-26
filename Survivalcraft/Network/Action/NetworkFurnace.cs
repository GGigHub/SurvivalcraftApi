using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkFurnace {
        public static void OnFurnaceUpdate(ComponentFurnace furnace) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new ComponentFurnacePacket(furnace.Entity.Id, furnace.FireTimeRemaining, furnace.SmeltingProgress, furnace.HeatLevel));
            }
        }

        public static void OnFurnacesUpdate(SubsystemFurnaceBlockBehavior subsystem, List<ComponentFurnace> furnaces) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new ComponentFurnacePacket(furnaces));
            }
        }
    }
}
