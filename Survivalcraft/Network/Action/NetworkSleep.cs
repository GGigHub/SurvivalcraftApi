using System;

namespace Game.Network {
    public static class NetworkSleep {
        public static void OnSleepEvent(ComponentSleep sleep, bool allowManualWakeup) {
            if (NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new SleepRequestPacket(sleep, allowManualWakeup));
            }
            else {
                NetworkManager.QueuePacket(new SleepStatusPacket(sleep, allowManualWakeup, true));
            }
        }

        public static void OnWakeUpEvent(ComponentSleep sleep) {
            if (NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new WakeupRequestPacket(sleep));
            }
            else {
                NetworkManager.QueuePacket(new WakeupStatusPacket(sleep));
            }
        }
    }
}
