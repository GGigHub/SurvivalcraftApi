namespace Survivalcraft.Testing.Control;

public sealed record CreatureBehaviorFixture(
    int ChaseEntityId,
    int DigInMudEntityId,
    int EatPickableEntityId,
    int FishOutOfWaterEntityId,
    int LookAroundEntityId,
    int RandomFeedEntityId,
    int RandomPeckEntityId,
    int StubbornSteedEntityId,
    int SoundEntityId) {
    public int[] EntityIds => [
        ChaseEntityId,
        DigInMudEntityId,
        EatPickableEntityId,
        FishOutOfWaterEntityId,
        LookAroundEntityId,
        RandomFeedEntityId,
        RandomPeckEntityId,
        StubbornSteedEntityId,
        SoundEntityId
    ];
}

public sealed record SurvivalStateFixture(
    int PlayerIndex,
    int PlayerEntityId,
    int ClothingInventoryId,
    int ClothingSlotIndex,
    int ClothingValue,
    string SkinName,
    CreatureBehaviorFixture Behaviors);

public sealed record PrepareSurvivalStateFixtureCommand(
    int PlayerIndex,
    string SkinName = "AutotestNetworkSkin.scskin");

public sealed record QuiesceSurvivalStateFixtureCommand(
    CreatureBehaviorFixture Fixture);

public sealed record SetCreatureBehaviorStateCommand(
    CreatureBehaviorFixture Fixture,
    bool Enabled = true,
    float HeadShakeOrder = 0.85f);

public enum CreatureSoundKind {
    Idle,
    Pain,
    Moan,
    Sneeze,
    Cough,
    Puke,
    Attack
}

public sealed record TriggerCreatureSoundCommand(
    int EntityId,
    CreatureSoundKind SoundKind = CreatureSoundKind.Idle);

public sealed record SetPlayerSurvivalStateCommand(
    SurvivalStateFixture Fixture,
    int ExperienceCount = 40,
    long BlocksDugIncrement = 7,
    float Food = 0.82f,
    float Stamina = 0.74f,
    float Sleep = 0.66f,
    float Wetness = 0.28f,
    float Temperature = 10.5f,
    bool TriggerFluSneeze = true,
    bool TriggerNausea = true);

public sealed record RequestSkinTransferCommand(
    int PlayerIndex,
    string SkinName);

public sealed record RunSurvivalStateRevisionBurstCommand(
    SurvivalStateFixture Fixture,
    bool SendStaleStates = true);

public sealed record ForgeSurvivalStateCommand(
    SurvivalStateFixture Fixture);

public sealed record CreatureBehaviorStateResult(
    ActionReceipt Receipt,
    long MinimumRevision,
    float HeadShakeOrder);

public sealed record PlayerSurvivalStateResult(
    ActionReceipt Receipt,
    double Level,
    long BlocksDug,
    int ClothingValue);

public sealed record SurvivalStateRevisionBurstResult(
    ActionReceipt Receipt,
    double FinalLevel,
    long FinalBlocksDug,
    float FinalFood,
    float FinalFluDuration,
    float FinalSicknessDuration,
    long MinimumFinalRevision);
