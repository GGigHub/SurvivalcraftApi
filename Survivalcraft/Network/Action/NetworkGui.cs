using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkGui {
        public static void OnPlayerCreativeFly(ComponentGui gui, ComponentPlayer player) {
            NetworkManager.QueuePacket(new PlayerCreativeFlyPacket(player));
        }

        public static void OnManualLightingStrike(ComponentGui gui, Vector3 position, Vector3 direction) {
            NetworkManager.QueuePacket(new SubsystemSkyPacket(position, direction));
        }

        public static void OnManualWeatherUpdate(ComponentGui gui, int type) {
            NetworkManager.QueuePacket(new SubsystemWeatherPacket(type));
        }

        public static void OnManualTimeUpdate(ComponentGui gui, double totalElapsedGameTime, double timeOfDayOffset) {
            NetworkManager.QueuePacket(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        }
    }
}
