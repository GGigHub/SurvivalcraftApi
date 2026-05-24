using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkScreen {
        public static void OnClientStateChanged(int id, ClientState state) {
            NetworkManager.QueuePacket(new ClientStateChangePacket((byte)id, state));
        }

        public static void OnPlayerIntoPlaying(ComponentPlayer player) {
            NetworkManager.QueuePacket(new PlayerIntoPlayingPacket(player));
        }

        public static void OnPlayerCreate(PlayerData playerData) {
            NetworkManager.QueuePacket(new PlayerCreatePacket(playerData));
        }

        public static void OnPlayerModify(PlayerData playerData) {
            NetworkManager.QueuePacket(new PlayerModifyPacket(playerData));
        }
    }
}
