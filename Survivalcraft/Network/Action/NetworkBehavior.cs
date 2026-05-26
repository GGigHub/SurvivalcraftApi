using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkBehavior {
        public static void OnChaseBehaviorAttackChanged(ComponentChaseBehavior behavior, bool isAttack) {
            if (!NetworkService.IsClient) {
                NetworkService.QueuePacket(new ChaseBehaviorPacket(behavior, isAttack));
            }
        }

        public static void OnDigInMudBehaviorChanged(ComponentDigInMudBehavior behavior, bool isDigIn) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new DigInMudBehaviorPacket(behavior, isDigIn));
            }
        }

        public static void OnEatPickableBehaviorChanged(ComponentEatPickableBehavior behavior, bool isFeed) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new EatPickableBehaviorPacket(behavior, isFeed));
            }
        }

        public static void OnFishOutOfWaterBehaviorChanged(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new FishOutOfWaterBehaviorPacket(behavior, isBend));
            }
        }

        public static void OnRandomFeedBehaviorChanged(ComponentRandomFeedBehavior behavior, bool isFeed) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new RandomFeedBehaviorPacket(behavior, isFeed));
            }
        }

        public static void OnRandomPeckBehaviorChanged(ComponentRandomPeckBehavior behavior, bool isPeck) {
            if (NetworkService.IsServer) {
                NetworkService.QueuePacket(new RandomPeckBehaviorPacket(behavior, isPeck));
            }
        }
    }
}
