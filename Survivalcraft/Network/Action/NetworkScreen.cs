using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkScreen {
        public static void OnNetworkStateChanged(NetworkState state) { int id = NetworkService.Self != null ? NetworkService.Self.ID : 0;
            NetworkService.QueuePacket(new NetworkStateChangePacket((byte)id, state));
        }

        public static void OnPlayerIntoPlaying() { ComponentPlayer player = NetworkService.MainPlayer;
            NetworkService.QueuePacket(new PlayerIntoPlayingPacket(player));
        }

        public static void OnPlayerCreate(PlayerData playerData) {
            NetworkService.QueuePacket(new PlayerCreatePacket(playerData));
        }

        public static void OnPlayerModify(PlayerData playerData) {
            NetworkService.QueuePacket(new PlayerModifyPacket(playerData));
        }
    }
}
