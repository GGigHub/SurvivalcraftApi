using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkBody {
        public static bool ShouldBeNetworkControlled(ComponentBody body) {
            if (!NetworkManager.IsClient && !NetworkManager.IsServer) return false;

            ComponentPlayer componentPlayer = body.Entity.FindComponent<ComponentPlayer>();
            bool isMainPlayer = componentPlayer?.PlayerData?.IsMainPlayer == true;

            bool hasChildPlayer = false;
            bool hasNonMainChildPlayer = false;
            for (int i = 0; i < body.ChildBodies.Count; i++) {
                ComponentPlayer childPlayer = body.ChildBodies[i].Entity.FindComponent<ComponentPlayer>();
                if (childPlayer != null) {
                    hasChildPlayer = true;
                    if (childPlayer.PlayerData?.IsMainPlayer != true) {
                        hasNonMainChildPlayer = true;
                        break;
                    }
                }
            }

            if (NetworkManager.IsClient) {
                return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
                    || (componentPlayer == null && !hasChildPlayer)
                    || hasNonMainChildPlayer;
            }
            else {
                return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
                    || (componentPlayer == null && hasNonMainChildPlayer);
            }
        }
    }
}
