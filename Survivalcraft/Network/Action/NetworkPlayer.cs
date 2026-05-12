using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkPlayer {
        public static void OnActiveSlotChanged(ComponentPlayer player) {
            if (player.ComponentMiner?.Inventory != null) {
                NetworkManager.QueuePacket(new ActiveSlotChangePacket(player.ComponentMiner.Inventory));
            }
        }

        public static void OnBodyUpdate(ComponentPlayer player, bool isMainPlayer) {
            if (!NetworkManager.IsClient && Time.PeriodicEvent(0.1f, 0.0)) {
                NetworkManager.QueuePacket(new PlayerBodyUpdatePacket(player) { Except = player.PlayerData.Client });
            }
            else if (isMainPlayer && Time.PeriodicEvent(0.1f, 0.0)) {
                NetworkManager.QueuePacket(new PlayerBodyUpdatePacket(player));
            }
        }

        public static void OnHit(ComponentPlayer player, ComponentBody hitBody, Vector3 hitPoint, Vector3 direction) {
            if (NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new PlayerHitPacket(player, hitBody, hitPoint, direction));
            }
        }

        public static void OnDigEvent(ComponentPlayer player) {
            NetworkManager.QueuePacket(new PlayerDigPacket(player));
        }

        public static void OnInteractEvent(ComponentPlayer player) {
            NetworkManager.QueuePacket(new PlayerInteractPacket(player));
        }

        public static void OnAimEvent(ComponentPlayer player) {
            NetworkManager.QueuePacket(new PlayerAimPacket(player));
        }
    }
}
