using System;

namespace Game.Network {
    public static class NetworkSleep {
        public static void OnSleepEvent(ComponentSleep sleep, bool allowManualWakeup) {
            if (NetworkService.IsClient) {
                NetworkService.QueuePacket(new SleepRequestPacket(sleep, allowManualWakeup));
            }
            else {
                NetworkService.QueuePacket(new SleepStatusPacket(sleep, allowManualWakeup, true));
            }
        }

        public static void OnWakeUpEvent(ComponentSleep sleep) {
            if (NetworkService.IsClient) {
                NetworkService.QueuePacket(new WakeupRequestPacket(sleep));
            }
            else {
                NetworkService.QueuePacket(new WakeupStatusPacket(sleep));
            }
        }
    }
}
