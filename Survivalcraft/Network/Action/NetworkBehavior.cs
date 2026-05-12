using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkBehavior {
        public static void OnChaseBehaviorAttackChanged(ComponentChaseBehavior behavior, bool isAttack) {
            if (!NetworkManager.IsClient) {
                NetworkManager.QueuePacket(new ChaseBehaviorPacket(behavior, isAttack));
            }
        }

        public static void OnDigInMudBehaviorChanged(ComponentDigInMudBehavior behavior, bool isDigIn) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new DigInMudBehaviorPacket(behavior, isDigIn));
            }
        }

        public static void OnEatPickableBehaviorChanged(ComponentEatPickableBehavior behavior, bool isFeed) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new EatPickableBehaviorPacket(behavior, isFeed));
            }
        }

        public static void OnFishOutOfWaterBehaviorChanged(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new FishOutOfWaterBehaviorPacket(behavior, isBend));
            }
        }

        public static void OnRandomFeedBehaviorChanged(ComponentRandomFeedBehavior behavior, bool isFeed) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new RandomFeedBehaviorPacket(behavior, isFeed));
            }
        }

        public static void OnRandomPeckBehaviorChanged(ComponentRandomPeckBehavior behavior, bool isPeck) {
            if (NetworkManager.IsServer) {
                NetworkManager.QueuePacket(new RandomPeckBehaviorPacket(behavior, isPeck));
            }
        }
    }
}
