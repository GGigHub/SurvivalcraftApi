using Engine;

namespace Game.Network;

public sealed class NetworkBehaviorState {
    public bool HasLocomotion;
    public Vector2? WalkOrder;
    public Vector3? FlyOrder;
    public Vector3? SwimOrder;
    public Vector2 TurnOrder;
    public float JumpOrder;

    public NetworkBehaviorState Clone() {
        return new NetworkBehaviorState {
            HasLocomotion = HasLocomotion,
            WalkOrder = WalkOrder,
            FlyOrder = FlyOrder,
            SwimOrder = SwimOrder,
            TurnOrder = TurnOrder,
            JumpOrder = JumpOrder
        };
    }
}

public enum NetworkBehaviorEventType : byte {
    ChaseAttackChanged,
    DigInMudChanged,
    EatPickableChanged,
    FishOutOfWaterChanged,
    HumanRow,
    LookAroundChanged,
    RandomFeedChanged,
    RandomPeckChanged,
    StubbornSteedHeadShake
}

public struct NetworkBehaviorEvent {
    public double Time;
    public int Sequence;
    public NetworkBehaviorEventType Type;
    public bool Value;
    public bool SecondaryValue;
    public float Amount;
}
