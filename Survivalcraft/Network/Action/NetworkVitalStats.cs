using System;
using Engine;

namespace Game.Network {
    public static class NetworkVitalStats {
        public static void OnVitalStatsSync(ComponentVitalStats stats) {
            if (NetworkManager.IsServer && Time.PeriodicEvent(1.0, 0.5)) {
                NetworkManager.QueuePacket(new VitalStatSyncPacket(stats));
            }
        }
    }
}
