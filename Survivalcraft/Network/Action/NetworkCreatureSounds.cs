using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkCreatureSounds {
        public static void OnPlaySound(ComponentCreatureSounds sounds, byte type, bool skipIfRecentlyPlayed) {
            if (!NetworkService.IsClient) {
                NetworkService.QueuePacket(new CreatureSoundPacket(sounds, type, skipIfRecentlyPlayed));
            }
        }
    }
}
