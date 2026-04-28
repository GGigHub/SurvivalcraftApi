using Engine;

namespace Game.NetWork.Packages;

public class ComponentBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.Behavior;
    public override ClientStage MinNeedState => ClientStage.Playing;

    public int m_entityId;

    public enum EventType {
        ChaseBehavior,
        RandomPeck,
        RandomFeed,
        EatPickable,
        HumanRow,
        CreatureSound,
        DigInMud,
        FishOutOfWater
    }

    public EventType eventType;
    public float? m_bend;
    public float m_digin;
    public bool m_rowLeft;
    public bool m_rowRight;
    public byte m_type;

    public ComponentBehaviorPackage() {
    }

    public ComponentBehaviorPackage(ComponentFishOutOfWaterBehavior fishOutOfWaterBehavior, bool isBend) {
        m_entityId = fishOutOfWaterBehavior.Entity.Id;
        eventType = EventType.FishOutOfWater;
        m_rowLeft = isBend;
    }

    public ComponentBehaviorPackage(ComponentChaseBehavior behavior, bool isChase) {
        m_entityId = behavior.Entity.Id;
        eventType = EventType.ChaseBehavior;
        m_rowLeft = isChase;
    }

    public ComponentBehaviorPackage(ComponentCreatureSounds creatureSound, byte type, bool skip = false) {
        m_entityId = creatureSound.Entity.Id;
        eventType = EventType.CreatureSound;
        m_type = type;
        m_rowLeft = skip;
    }

    public ComponentBehaviorPackage(ComponentRandomPeckBehavior behavior, bool inRange) {
        m_entityId = behavior.Entity.Id;
        eventType = EventType.RandomPeck;
        m_rowLeft = inRange;
    }

    public ComponentBehaviorPackage(ComponentRandomFeedBehavior behavior, bool inRange) {
        m_entityId = behavior.Entity.Id;
        eventType = EventType.RandomFeed;
        m_rowLeft = inRange;
    }

    public ComponentBehaviorPackage(ComponentEatPickableBehavior behavior, bool inRange) {
        m_entityId = behavior.Entity.Id;
        eventType = EventType.EatPickable;
        m_rowLeft = inRange;
    }

    public ComponentBehaviorPackage(ComponentDigInMudBehavior behavior, bool isDigIn) {
        m_entityId = behavior.Entity.Id;
        eventType = EventType.DigInMud;
        m_rowLeft = isDigIn;
    }

    public ComponentBehaviorPackage(ComponentHumanModel componentModel) {
        eventType = EventType.HumanRow;
        m_entityId = componentModel.Entity.Id;
        m_rowLeft = componentModel.m_rowLeft;
        m_rowRight = componentModel.m_rowRight;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref eventType);
        archive.Value(ref m_entityId);
        switch (eventType) {
            case EventType.FishOutOfWater:
            case EventType.DigInMud:
            case EventType.ChaseBehavior:
            case EventType.RandomFeed:
            case EventType.RandomPeck:
            case EventType.EatPickable:
                archive.Value(ref m_rowLeft);
                break;
            case EventType.HumanRow:
                archive.Value(ref m_rowLeft);
                archive.Value(ref m_rowRight);
                break;
            case EventType.CreatureSound:
                archive.Value(ref m_type);
                archive.Value(ref m_rowLeft);
                break;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer && eventType != EventType.CreatureSound) {
            return;
        }
        GameManager.Project.FindEntity(m_entityId, entity => {
            switch (eventType) {
                case EventType.ChaseBehavior:
                    ComponentChaseBehavior chaseBehavior = entity.FindComponent<ComponentChaseBehavior>();
                    if (chaseBehavior != null) {
                        chaseBehavior.IsAttack = m_rowLeft;
                    }
                    else {
#if DEBUG
                        Log.Information($"澶勭悊琛屼负ChaseBehavior澶辫触锛屽疄浣揫{m_entityId}]{entity.ValuesDictionary.DatabaseObject.Name}娌℃湁瀵瑰簲琛屼负");
#endif
                    }
                    break;
                case EventType.RandomFeed:
                    ComponentRandomFeedBehavior randomFeedBehavior = entity.FindComponent<ComponentRandomFeedBehavior>();
                    if (randomFeedBehavior != null) {
                        randomFeedBehavior.IsFeed = m_rowLeft;
                    }
                    else {
#if DEBUG
                        Log.Information($"澶勭悊琛屼负RandomFeed澶辫触锛屽疄浣揫{m_entityId}]{entity.ValuesDictionary.DatabaseObject.Name}娌℃湁瀵瑰簲琛屼负");
#endif
                    }
                    break;
                case EventType.RandomPeck:
                    ComponentRandomPeckBehavior peck = entity.FindComponent<ComponentRandomPeckBehavior>();
                    if (peck != null) {
                        peck.IsFeed = m_rowLeft;
                    }
                    else {
#if DEBUG
                        Log.Information($"澶勭悊琛屼负RandomPeck澶辫触锛屽疄浣揫{m_entityId}]{entity.ValuesDictionary.DatabaseObject.Name}娌℃湁瀵瑰簲琛屼负");
#endif
                    }
                    break;
                case EventType.EatPickable:
                    ComponentEatPickableBehavior eatPickableBehavior = entity.FindComponent<ComponentEatPickableBehavior>();
                    if (eatPickableBehavior != null) {
                        eatPickableBehavior.IsFeed = m_rowLeft;
                    }
                    break;
                case EventType.DigInMud:
                    ComponentDigInMudBehavior digInMudBehavior = entity.FindComponent<ComponentDigInMudBehavior>();
                    if (digInMudBehavior != null) {
                        digInMudBehavior.IsDigIn = m_rowLeft;
                    }
                    break;
                case EventType.FishOutOfWater:
                    ComponentFishOutOfWaterBehavior fishOutOfWaterBehavior = entity.FindComponent<ComponentFishOutOfWaterBehavior>();
                    if (fishOutOfWaterBehavior != null) {
                        fishOutOfWaterBehavior.IsBend = m_rowLeft;
                    }
                    break;
                case EventType.HumanRow:
                    ComponentHumanModel humanModel = entity.FindComponent<ComponentHumanModel>();
                    if (humanModel != null) {
                        humanModel.hasData = true;
                        humanModel.m_rowLeft = m_rowLeft;
                        humanModel.m_rowRight = m_rowRight;
                        var random = new Random();
                        GameManager.Project.FindSubsystem<SubsystemAudio>().PlayRandomSound("Audio/Rowing", random.Float(0.4f, 0.6f), random.Float(-0.3f, 0.2f), humanModel.m_componentCreature.ComponentBody.Position, 3f, autoDelay: true);
                        if (isServer) {
                            Except = From;
                            NetworkManager.QueuePackage(this);
                        }
                    }
                    break;
                case EventType.CreatureSound:
                    ComponentCreatureSounds creatureSound = entity.FindComponent<ComponentCreatureSounds>();
                    if (creatureSound != null) {
                        switch (m_type) {
                            case 0:
                                creatureSound.PlayIdleSoundLogic(m_rowLeft);
                                break;
                            case 1:
                                creatureSound.PlayPainSoundLogic();
                                break;
                            case 2:
                                creatureSound.PlayMoanSoundLogic();
                                break;
                            case 3:
                                creatureSound.PlaySneezeSoundLogic();
                                break;
                            case 4:
                                creatureSound.PlayCoughSoundLogic();
                                break;
                            case 5:
                                creatureSound.PlayPukeSoundLogic();
                                break;
                            case 6:
                                creatureSound.PlayAttackSoundLogic();
                                break;
                        }
                    }
                    break;
            }
        });
    }
}
