using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class NetworkBehaviorSyncHelpers {
    public static void ApplyState(ComponentBody body, NetworkBehaviorState state) {
        if (body?.Entity == null || state == null) {
            return;
        }

        Entity entity = body.Entity;
        if (state.HasLocomotion) {
            ComponentLocomotion locomotion = entity.FindComponent<ComponentLocomotion>();
            if (locomotion != null) {
                locomotion.LastWalkOrder = state.WalkOrder;
                locomotion.LastFlyOrder = state.FlyOrder;
                locomotion.LastSwimOrder = state.SwimOrder;
                locomotion.LastTurnOrder = state.TurnOrder;
                locomotion.LastJumpOrder = state.JumpOrder;
                locomotion.WalkOrder = null;
                locomotion.FlyOrder = null;
                locomotion.SwimOrder = null;
                locomotion.TurnOrder = Vector2.Zero;
                locomotion.JumpOrder = 0f;
            }
        }
    }

    public static void ApplyEvent(ComponentBody body, NetworkBehaviorEvent behaviorEvent) {
        if (body?.Entity == null) {
            return;
        }

        Entity entity = body.Entity;
        switch (behaviorEvent.Type) {
            case NetworkBehaviorEventType.ChaseAttackChanged:
                ComponentChaseBehavior chase = entity.FindComponent<ComponentChaseBehavior>();
                if (chase != null) {
                    chase.IsAttack = behaviorEvent.Value;
                }
                break;
            case NetworkBehaviorEventType.DigInMudChanged:
                ComponentDigInMudBehavior digInMud = entity.FindComponent<ComponentDigInMudBehavior>();
                if (digInMud != null) {
                    digInMud.IsDigIn = behaviorEvent.Value;
                }
                break;
            case NetworkBehaviorEventType.EatPickableChanged:
                ComponentEatPickableBehavior eatPickable = entity.FindComponent<ComponentEatPickableBehavior>();
                if (eatPickable != null) {
                    eatPickable.IsFeed = behaviorEvent.Value;
                }
                break;
            case NetworkBehaviorEventType.FishOutOfWaterChanged:
                ComponentFishOutOfWaterBehavior fishOutOfWater = entity.FindComponent<ComponentFishOutOfWaterBehavior>();
                if (fishOutOfWater != null) {
                    fishOutOfWater.IsBend = behaviorEvent.Value;
                }
                break;
            case NetworkBehaviorEventType.HumanRow:
                ComponentHumanModel humanModel = entity.FindComponent<ComponentHumanModel>();
                if (humanModel != null) {
                    ApplyHumanRow(humanModel, behaviorEvent.Value, behaviorEvent.SecondaryValue);
                }
                break;
            case NetworkBehaviorEventType.LookAroundChanged:
                ComponentLookAroundBehavior lookAround = entity.FindComponent<ComponentLookAroundBehavior>();
                if (lookAround != null) {
                    lookAround.IsLookRandom = behaviorEvent.Value;
                }
                break;
            case NetworkBehaviorEventType.RandomFeedChanged:
                ComponentRandomFeedBehavior randomFeed = entity.FindComponent<ComponentRandomFeedBehavior>();
                if (randomFeed != null) {
                    randomFeed.IsFeed = behaviorEvent.Value;
                }
                break;
            case NetworkBehaviorEventType.RandomPeckChanged:
                ComponentRandomPeckBehavior randomPeck = entity.FindComponent<ComponentRandomPeckBehavior>();
                if (randomPeck != null) {
                    randomPeck.IsFeed = behaviorEvent.Value;
                }
                break;
            case NetworkBehaviorEventType.StubbornSteedHeadShake:
                ComponentCreatureModel creatureModel = entity.FindComponent<ComponentCreatureModel>();
                if (creatureModel != null) {
                    creatureModel.HeadShakeOrder = behaviorEvent.Amount;
                }
                break;
        }
    }

    private static void ApplyHumanRow(ComponentHumanModel humanModel, bool rowLeft, bool rowRight) {
        humanModel.hasData = true;
        humanModel.m_rowHoldEndTime = GameManager.Project.FindSubsystem<SubsystemGameInfo>().TotalElapsedGameTime + 0.75;
        humanModel.m_rowLeft = rowLeft;
        humanModel.m_rowRight = rowRight;
        humanModel.RowLeftOrder = rowLeft;
        humanModel.RowRightOrder = rowRight;
        Random random = new();
        GameManager.Project.FindSubsystem<SubsystemAudio>().PlayRandomSound(
            "Audio/Rowing",
            random.Float(0.4f, 0.6f),
            random.Float(-0.3f, 0.2f),
            humanModel.m_componentCreature.ComponentBody.Position,
            3f,
            autoDelay: true);
    }

}
