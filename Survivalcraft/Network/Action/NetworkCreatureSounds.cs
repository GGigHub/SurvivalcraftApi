using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkCreatureSounds {
        public static void OnPlaySound(ComponentCreatureSounds sounds, byte type, bool skipIfRecentlyPlayed) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new CreatureSoundPacket(sounds, type, skipIfRecentlyPlayed));
            }
        }
    }
}
