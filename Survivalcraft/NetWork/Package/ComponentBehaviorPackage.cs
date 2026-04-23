using Engine;

namespace Game.NetWork.Packages;

public class ComponentBehaviorPackage : IPackage {
    public byte ID => (byte)PackageType.ComponentBehavior;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.Playing;

    public ushort m_entityId;

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

    public ComponentBehaviorPackage() { }

    public ComponentBehaviorPackage(ComponentFishOutOfWaterBehavior fishOutOfWaterBehavior, bool isBend) {
        m_entityId = fishOutOfWaterBehavior.Entity.EntityId;
        eventType = EventType.FishOutOfWater;
        m_rowLeft = isBend;
    }

    public ComponentBehaviorPackage(ComponentChaseBehavior behavior, bool isChase) {
        m_entityId = behavior.Entity.EntityId;
        eventType = EventType.ChaseBehavior;
        m_rowLeft = isChase;
    }
    public ComponentBehaviorPackage(ComponentCreatureSounds creatureSound, byte type, bool skip = false) {
        m_entityId = creatureSound.Entity.EntityId;
        eventType = EventType.CreatureSound;
        m_type = type;
        m_rowLeft = skip;
    }
    public ComponentBehaviorPackage(ComponentRandomPeckBehavior behavior, bool inRange) {
        m_entityId = behavior.Entity.EntityId;
        eventType = EventType.RandomPeck;
        m_rowLeft = inRange;
    }
    public ComponentBehaviorPackage(ComponentRandomFeedBehavior behavior, bool inRange) {
        m_entityId = behavior.Entity.EntityId;
        eventType = EventType.RandomFeed;
        m_rowLeft = inRange;
    }

    public ComponentBehaviorPackage(ComponentEatPickableBehavior behavior, bool inRange) {
        m_entityId = behavior.Entity.EntityId;
        eventType = EventType.EatPickable;
        m_rowLeft = inRange;
    }
    public ComponentBehaviorPackage(ComponentDigInMudBehavior behavior, bool IsDigIn) {
        m_entityId = behavior.Entity.EntityId;
        eventType = EventType.DigInMud;
        m_rowLeft = IsDigIn;
    }
    public ComponentBehaviorPackage(ComponentHumanModel componentModel) {
        eventType = EventType.HumanRow;
        m_entityId = componentModel.Entity.EntityId;
        m_rowLeft = componentModel.m_rowLeft;
        m_rowRight = componentModel.m_rowRight;
    }
    public void WriteData(PackageStreamWriter writer) {
        writer.WriteEnum(eventType);
        writer.Write(m_entityId);
        switch (eventType) {
            case EventType.FishOutOfWater:
            case EventType.DigInMud:
            case EventType.ChaseBehavior:
            case EventType.RandomFeed:
            case EventType.RandomPeck:
            case EventType.EatPickable:
                writer.Write(m_rowLeft);
                break;
            case EventType.HumanRow:
                writer.Write(m_rowLeft);
                writer.Write(m_rowRight);
                break;
            case EventType.CreatureSound:
                writer.Write(m_type);
                writer.Write(m_rowLeft);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader) {
        eventType = reader.ReadEnum<EventType>();
        m_entityId = reader.ReadUInt16();
        switch (eventType) {
            case EventType.FishOutOfWater:
            case EventType.DigInMud:
            case EventType.ChaseBehavior:
            case EventType.RandomFeed:
            case EventType.RandomPeck:
            case EventType.EatPickable:
                m_rowLeft = reader.ReadBoolean();
                break;
            case EventType.HumanRow:
                m_rowLeft = reader.ReadBoolean();
                m_rowRight = reader.ReadBoolean();
                break;
            case EventType.CreatureSound:
                m_type = reader.ReadByte();
                m_rowLeft = reader.ReadBoolean();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer) {
        if (isServer && eventType != EventType.CreatureSound) {
            return;
        }
        projectNet.FindEntityById(m_entityId, (entity) => {
            switch (eventType) {
                case EventType.ChaseBehavior:
                    ComponentChaseBehavior chaseBehavior = entity.FindComponent<ComponentChaseBehavior>();
                    if (chaseBehavior != null) {
                        chaseBehavior.IsAttack = m_rowLeft;
                    }
                    else {
#if DEBUG
                        Log.Information($"处理行为ChaseBehavior失败，实体[{m_entityId}]{entity.ValuesDictionary.DatabaseObject.Name}没有对应行为");
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
                        Log.Information($"处理行为RandomFeed失败，实体[{m_entityId}]{entity.ValuesDictionary.DatabaseObject.Name}没有对应行为");
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
                        Log.Information($"处理行为RandomPeck失败，实体[{m_entityId}]{entity.ValuesDictionary.DatabaseObject.Name}没有对应行为");
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
                        var m_random = new Random();
                        projectNet.FindSubsystem<SubsystemAudio>().PlayRandomSound("Audio/Rowing", m_random.Float(0.4f, 0.6f), m_random.Float(-0.3f, 0.2f), humanModel.m_componentCreature.ComponentBody.Position, 3f, autoDelay: true);
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

