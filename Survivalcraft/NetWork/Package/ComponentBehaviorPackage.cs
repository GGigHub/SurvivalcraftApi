using Engine;
using GameEntitySystem;

namespace Game.NetWork.Packages;

internal static class BehaviorPackageHelpers {
    public static void WithEntity(int entityId, Action<Entity> action) {
        GameManager.Project.FindEntity(entityId, action);
    }

    public static void BroadcastFromServer(IPackage package, bool isServer) {
        if (isServer) {
            package.Except = package.From;
            NetworkManager.QueuePackage(package);
        }
    }
}

public class ChaseBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorChase;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isAttack;

    public ChaseBehaviorPackage() { }
    public ChaseBehaviorPackage(ComponentChaseBehavior behavior, bool isAttack) {
        m_entityId = behavior.Entity.Id;
        m_isAttack = isAttack;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isAttack);
    }
    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentChaseBehavior behavior = entity.FindComponent<ComponentChaseBehavior>();
            if (behavior != null) {
                behavior.IsAttack = m_isAttack;
            }
        });
    }
}

public class RandomPeckBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorRandomPeck;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public RandomPeckBehaviorPackage() { }
    public RandomPeckBehaviorPackage(ComponentRandomPeckBehavior behavior, bool isFeed) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentRandomPeckBehavior behavior = entity.FindComponent<ComponentRandomPeckBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}

public class RandomFeedBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorRandomFeed;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public RandomFeedBehaviorPackage() { }
    public RandomFeedBehaviorPackage(ComponentRandomFeedBehavior behavior, bool isFeed) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentRandomFeedBehavior behavior = entity.FindComponent<ComponentRandomFeedBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}

public class EatPickableBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorEatPickable;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public EatPickableBehaviorPackage() { }
    public EatPickableBehaviorPackage(ComponentEatPickableBehavior behavior, bool isFeed) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentEatPickableBehavior behavior = entity.FindComponent<ComponentEatPickableBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}

public class HumanRowBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorHumanRow;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_rowLeft;
    public bool m_rowRight;
    public HumanRowBehaviorPackage() { }
    public HumanRowBehaviorPackage(ComponentHumanModel model) {
        m_entityId = model.Entity.Id;
        m_rowLeft = model.m_rowLeft;
        m_rowRight = model.m_rowRight;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_rowLeft);
        archive.Value(ref m_rowRight);
    }
    public override void Handle(bool isServer) {
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentHumanModel humanModel = entity.FindComponent<ComponentHumanModel>();
            if (humanModel != null) {
                humanModel.hasData = true;
                humanModel.m_rowLeft = m_rowLeft;
                humanModel.m_rowRight = m_rowRight;
                Random random = new();
                GameManager.Project.FindSubsystem<SubsystemAudio>().PlayRandomSound("Audio/Rowing", random.Float(0.4f, 0.6f), random.Float(-0.3f, 0.2f), humanModel.m_componentCreature.ComponentBody.Position, 3f, autoDelay: true);
                BehaviorPackageHelpers.BroadcastFromServer(this, isServer);
            }
        });
    }
}

public class CreatureSoundPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorCreatureSound;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public byte m_type;
    public bool m_skipIfRecentlyPlayed;
    public CreatureSoundPackage() { }
    public CreatureSoundPackage(ComponentCreatureSounds creatureSound, byte type, bool skip = false) {
        m_entityId = creatureSound.Entity.Id;
        m_type = type;
        m_skipIfRecentlyPlayed = skip;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_type);
        archive.Value(ref m_skipIfRecentlyPlayed);
    }
    public override void Handle(bool isServer) {
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentCreatureSounds creatureSound = entity.FindComponent<ComponentCreatureSounds>();
            if (creatureSound == null) {
                return;
            }
            if (m_type == 0) creatureSound.PlayIdleSoundLogic(m_skipIfRecentlyPlayed);
            else if (m_type == 1) creatureSound.PlayPainSoundLogic();
            else if (m_type == 2) creatureSound.PlayMoanSoundLogic();
            else if (m_type == 3) creatureSound.PlaySneezeSoundLogic();
            else if (m_type == 4) creatureSound.PlayCoughSoundLogic();
            else if (m_type == 5) creatureSound.PlayPukeSoundLogic();
            else if (m_type == 6) creatureSound.PlayAttackSoundLogic();
        });
    }
}

public class DigInMudBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorDigInMud;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isDigIn;
    public DigInMudBehaviorPackage() { }
    public DigInMudBehaviorPackage(ComponentDigInMudBehavior behavior, bool isDigIn) {
        m_entityId = behavior.Entity.Id;
        m_isDigIn = isDigIn;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isDigIn);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentDigInMudBehavior behavior = entity.FindComponent<ComponentDigInMudBehavior>();
            if (behavior != null) {
                behavior.IsDigIn = m_isDigIn;
            }
        });
    }
}

public class FishOutOfWaterBehaviorPackage : IPackage {
    public override byte ID => (byte)PackageType.BehaviorFishOutOfWater;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isBend;
    public FishOutOfWaterBehaviorPackage() { }
    public FishOutOfWaterBehaviorPackage(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
        m_entityId = behavior.Entity.Id;
        m_isBend = isBend;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isBend);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPackageHelpers.WithEntity(m_entityId, entity => {
            ComponentFishOutOfWaterBehavior behavior = entity.FindComponent<ComponentFishOutOfWaterBehavior>();
            if (behavior != null) {
                behavior.IsBend = m_isBend;
            }
        });
    }
}
