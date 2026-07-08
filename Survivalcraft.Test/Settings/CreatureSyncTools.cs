using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class CreatureSyncTools {
    // ----------------------------------------
    // Behavior Sync Tools
    // ----------------------------------------
    public static string AddBehaviorSyncEntitiesCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/AddBehaviorSyncEntitiesCode.cs");

    public static async Task<Dictionary<string, int>> AddBehaviorSyncEntitiesAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(AddBehaviorSyncEntitiesCode) ?? string.Empty;
        return ParseBehaviorEntityIds(raw);
    }

    public static async Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            bool exists = (bool?)await context.PushCommandAsync(
                string.Create(CultureInfo.InvariantCulture, $"FUNC return Game.GameManager.Project.FindEntity({entityId}) != null;")) ?? false;
            if (exists) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    public static async Task WaitForEntitiesAsync(GameContext context, IEnumerable<int> entityIds) {
        foreach (int entityId in entityIds.Distinct()) {
            Assert.True(await WaitForEntityExistsAsync(context, entityId), $"Client did not receive behavior test entity {entityId}.");
        }
    }

    public static string BuildConfigureCreatureSoundStateCode(int entityId) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ConfigureCreatureSoundStateCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));

    public static string BuildTriggerCreatureSoundCode(int entityId, CreatureSoundType soundType) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/TriggerCreatureSoundCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))
            .Replace("__SOUND_TYPE__", ((int)soundType).ToString(CultureInfo.InvariantCulture));

    public static string BuildGetCreatureSoundStateCode(int entityId, CreatureSoundType soundType) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/GetCreatureSoundStateCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))
            .Replace("__SOUND_TYPE__", ((int)soundType).ToString(CultureInfo.InvariantCulture));

    public static string BuildForceBehaviorSyncStateCode(IReadOnlyDictionary<string, int> entityIds) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceBehaviorSyncStateCode.cs")
            .Replace("__CHASE_ID__", entityIds["Chase"].ToString(CultureInfo.InvariantCulture))
            .Replace("__DIG_IN_MUD_ID__", entityIds["DigInMud"].ToString(CultureInfo.InvariantCulture))
            .Replace("__EAT_PICKABLE_ID__", entityIds["EatPickable"].ToString(CultureInfo.InvariantCulture))
            .Replace("__FISH_OUT_OF_WATER_ID__", entityIds["FishOutOfWater"].ToString(CultureInfo.InvariantCulture))
            .Replace("__LOOK_AROUND_ID__", entityIds["LookAround"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_FEED_ID__", entityIds["RandomFeed"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_PECK_ID__", entityIds["RandomPeck"].ToString(CultureInfo.InvariantCulture))
            .Replace("__STUBBORN_STEED_ID__", entityIds["StubbornSteed"].ToString(CultureInfo.InvariantCulture));

    public static string BuildGetBehaviorSyncStateCode(IReadOnlyDictionary<string, int> entityIds) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/GetBehaviorSyncStateCode.cs")
            .Replace("__CHASE_ID__", entityIds["Chase"].ToString(CultureInfo.InvariantCulture))
            .Replace("__DIG_IN_MUD_ID__", entityIds["DigInMud"].ToString(CultureInfo.InvariantCulture))
            .Replace("__EAT_PICKABLE_ID__", entityIds["EatPickable"].ToString(CultureInfo.InvariantCulture))
            .Replace("__FISH_OUT_OF_WATER_ID__", entityIds["FishOutOfWater"].ToString(CultureInfo.InvariantCulture))
            .Replace("__LOOK_AROUND_ID__", entityIds["LookAround"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_FEED_ID__", entityIds["RandomFeed"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_PECK_ID__", entityIds["RandomPeck"].ToString(CultureInfo.InvariantCulture))
            .Replace("__STUBBORN_STEED_ID__", entityIds["StubbornSteed"].ToString(CultureInfo.InvariantCulture));

    public static async Task ConfigureCreatureSoundStateAsync(GameContext context, int entityId) {
        string result = (string?)await context.PushCommandAsync(BuildConfigureCreatureSoundStateCode(entityId)) ?? string.Empty;
        Assert.Equal("ok", result);
    }

    public static async Task TriggerCreatureSoundAsync(GameContext context, int entityId, CreatureSoundType soundType) {
        string result = (string?)await context.PushCommandAsync(BuildTriggerCreatureSoundCode(entityId, soundType)) ?? string.Empty;
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"ok:{(int)soundType}"), result);
    }

    public static async Task<CreatureSoundState> GetCreatureSoundStateAsync(GameContext context, int entityId, CreatureSoundType soundType) {
        string raw = (string?)await context.PushCommandAsync(BuildGetCreatureSoundStateCode(entityId, soundType)) ?? string.Empty;
        return ParseCreatureSoundState(soundType, raw);
    }

    public static async Task<CreatureSoundState> WaitForCreatureSoundPlayedAsync(GameContext context, int entityId, CreatureSoundType soundType) {
        CreatureSoundState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetCreatureSoundStateAsync(context, entityId, soundType);
            if (state.Played) {
                return state;
            }
            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<BehaviorSyncState> ForceBehaviorSyncStateAsync(GameContext context, IReadOnlyDictionary<string, int> entityIds) {
        string raw = (string?)await context.PushCommandAsync(BuildForceBehaviorSyncStateCode(entityIds)) ?? string.Empty;
        return ParseBehaviorSyncState(raw);
    }

    public static async Task<BehaviorSyncState> GetBehaviorSyncStateAsync(GameContext context, IReadOnlyDictionary<string, int> entityIds) {
        string raw = (string?)await context.PushCommandAsync(BuildGetBehaviorSyncStateCode(entityIds)) ?? string.Empty;
        return ParseBehaviorSyncState(raw);
    }

    public static async Task<BehaviorSyncState> WaitForBehaviorSyncStateAsync(GameContext context, IReadOnlyDictionary<string, int> entityIds) {
        BehaviorSyncState merged = default;
        for (int i = 0; i < 50; i++) {
            merged = MergeBehaviorSyncStates(merged, await GetBehaviorSyncStateAsync(context, entityIds));
            if (HasExpectedBehaviorSyncState(merged)) {
                return merged;
            }
            await Task.Delay(100);
        }

        return merged;
    }

    public static Dictionary<string, int> ParseBehaviorEntityIds(string value) {
        Dictionary<string, int> result = [];
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] pair = item.Split(':', 2);
            if (pair.Length == 2
                && int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entityId)) {
                result[pair[0]] = entityId;
            }
        }

        return result;
    }

    public static CreatureSoundState ParseCreatureSoundState(CreatureSoundType soundType, string value) {
        CreatureSoundState result = new() { SoundType = soundType, Raw = value };
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] pair = item.Split('=', 2);
            if (pair.Length != 2) {
                continue;
            }

            if (pair[0] == "played") {
                result.Played = pair[1] == "1";
            }
            else if (pair[0] == "last"
                && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lastSoundTime)) {
                result.LastSoundTime = lastSoundTime;
            }
            else if (pair[0] == "cough"
                && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lastCoughingSoundTime)) {
                result.LastCoughingSoundTime = lastCoughingSoundTime;
            }
            else if (pair[0] == "puke"
                && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lastPukeSoundTime)) {
                result.LastPukeSoundTime = lastPukeSoundTime;
            }
        }

        return result;
    }

    public static BehaviorSyncState ParseBehaviorSyncState(string value) {
        BehaviorSyncState result = new() { Raw = value };
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] parts = item.Split(':', 3);
            if (parts.Length != 3
                || parts[2].StartsWith("missing", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            Dictionary<string, string> values = parts[2]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

            bool HasFlag(string key) =>
                values.TryGetValue(key, out string? flagValue)
                && flagValue == "1";

            float GetFloat(string key) =>
                values.TryGetValue(key, out string? floatValue)
                && float.TryParse(floatValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    ? parsed
                    : 0f;

            switch (parts[0]) {
                case "Chase":
                    result.ChaseIsAttack = HasFlag("attack");
                    break;
                case "DigInMud":
                    result.DigInMudIsDigIn = HasFlag("dig");
                    break;
                case "EatPickable":
                    result.EatPickableIsFeed = HasFlag("feed");
                    break;
                case "FishOutOfWater":
                    result.FishOutOfWaterIsBend = HasFlag("bend");
                    break;
                case "LookAround":
                    result.LookAroundIsLookRandom = HasFlag("look");
                    break;
                case "RandomFeed":
                    result.RandomFeedIsFeed = HasFlag("feed");
                    break;
                case "RandomPeck":
                    result.RandomPeckIsFeed = HasFlag("feed");
                    break;
                case "StubbornSteed":
                    result.StubbornSteedHeadShakeOrder = GetFloat("shake");
                    break;
            }
        }

        return result;
    }

    public static bool HasExpectedBehaviorSyncState(BehaviorSyncState state) =>
        state.ChaseIsAttack
        && state.DigInMudIsDigIn
        && state.EatPickableIsFeed
        && state.FishOutOfWaterIsBend
        && state.LookAroundIsLookRandom
        && state.RandomFeedIsFeed
        && state.RandomPeckIsFeed
        && state.StubbornSteedHeadShakeOrder > 0f;

    public static void AssertExpectedBehaviorSyncState(BehaviorSyncState state, string contextName) {
        Assert.True(state.ChaseIsAttack, $"{contextName} Chase did not set IsAttack. State={state}.");
        Assert.True(state.DigInMudIsDigIn, $"{contextName} DigInMud did not set IsDigIn. State={state}.");
        Assert.True(state.EatPickableIsFeed, $"{contextName} EatPickable did not set IsFeed. State={state}.");
        Assert.True(state.FishOutOfWaterIsBend, $"{contextName} FishOutOfWater did not set IsBend. State={state}.");
        Assert.True(state.LookAroundIsLookRandom, $"{contextName} LookAround did not set IsLookRandom. State={state}.");
        Assert.True(state.RandomFeedIsFeed, $"{contextName} RandomFeed did not set IsFeed. State={state}.");
        Assert.True(state.RandomPeckIsFeed, $"{contextName} RandomPeck did not set IsFeed. State={state}.");
        Assert.True(state.StubbornSteedHeadShakeOrder > 0f, $"{contextName} StubbornSteed did not set HeadShakeOrder. State={state}.");
    }

    public static BehaviorSyncState MergeBehaviorSyncStates(BehaviorSyncState state1, BehaviorSyncState state2) =>
        new(
            state1.ChaseIsAttack || state2.ChaseIsAttack,
            state1.DigInMudIsDigIn || state2.DigInMudIsDigIn,
            state1.EatPickableIsFeed || state2.EatPickableIsFeed,
            state1.FishOutOfWaterIsBend || state2.FishOutOfWaterIsBend,
            state1.LookAroundIsLookRandom || state2.LookAroundIsLookRandom,
            state1.RandomFeedIsFeed || state2.RandomFeedIsFeed,
            state1.RandomPeckIsFeed || state2.RandomPeckIsFeed,
            MathF.Max(state1.StubbornSteedHeadShakeOrder, state2.StubbornSteedHeadShakeOrder)) {
            Raw = state2.Raw
        };

    // ----------------------------------------
    // Body Collision & Position Sync Tools
    // ----------------------------------------
    public const float PositionEpsilonSquared = 0.04f;
    public const float PlayerPositionEpsilonSquared = 0.09f;
    public const float VelocityEpsilon = 0.02f;
    public const float CollisionPreparationPositionEpsilonSquared = 0.36f;

    public static Task<int> CreateBoatAsync(GameContext context, float xOffset, float yOffset, float zOffset) =>
        EntitySyncTools.CreateBoatAsync(context, xOffset, yOffset, zOffset);

    public static async Task<int> CreateBoatNearPlayerAsync(
        GameContext context,
        int playerIndex,
        float offsetX,
        float offsetY,
        float offsetZ) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("CreatureSync/CreateBoatNearPlayerCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_X__", offsetX.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Y__", offsetY.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Z__", offsetZ.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<BodySyncState> GetBodyStateAsync(GameContext context, int entityId) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("CreatureSync/GetBodyStateCode.cs")
                .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseBodyState(raw);
    }

    public static async Task<BodySyncState> GetPlayerBodyStateAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("CreatureSync/GetPlayerBodyStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseBodyState(raw);
    }

    public static async Task<BodySyncState> PrepareMainPlayerForBodyCollisionAsync(
        GameContext context,
        int targetEntityId,
        Vector3Dto offset,
        Vector3Dto velocity) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("CreatureSync/PrepareMainPlayerForBodyCollisionCode.cs")
                .Replace("__TARGET_ENTITY_ID__", targetEntityId.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_X__", offset.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Y__", offset.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Z__", offset.Z.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_X__", velocity.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_Y__", velocity.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_Z__", velocity.Z.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseBodyState(raw);
    }

    public static Task HoldMainPlayerMoveInputAsync(GameContext context, Vector3Dto move, int frameCount) =>
        context.PushCommandAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"MOVE_INPUT {move.X} {move.Y} {move.Z} {frameCount}"));

    public static async Task<BodySyncState> WaitForPlayerBodyAsync(GameContext context, int playerIndex) {
        BodySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetPlayerBodyStateAsync(context, playerIndex);
            if (state.Exists) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Player body was not available for player {playerIndex}. Actual={state}.");
    }

    public static async Task<BodySyncState> WaitForBodyVelocityXAsync(
        GameContext context,
        int entityId,
        float minVelocityX) {
        BodySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetBodyStateAsync(context, entityId);
            if (state.Exists && state.Velocity.X >= minVelocityX) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Body {entityId} did not receive collision velocity X >= {minVelocityX:R}. Actual={state}.");
    }

    public static async Task<BodySyncState> WaitForBodyHorizontalSpeedAsync(
        GameContext context,
        int entityId,
        float minSpeed) {
        BodySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetBodyStateAsync(context, entityId);
            if (state.Exists && HorizontalSpeed(state.Velocity) >= minSpeed) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Body {entityId} did not receive horizontal collision speed >= {minSpeed:R}. Actual={state}.");
    }

    public static async Task<BodySyncState> WaitForBodyStateNearAsync(GameContext context, int entityId, BodySyncState expected) {
        BodySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetBodyStateAsync(context, entityId);
            if (BodyStatesNear(expected, state)) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Body {entityId} did not synchronize near expected state. Expected={expected}, Actual={state}.");
    }

    public static async Task<BodySyncState> WaitForPlayerNearAsync(GameContext context, int playerIndex, BodySyncState expected) {
        BodySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetPlayerBodyStateAsync(context, playerIndex);
            if (PlayerBodyStatesNear(expected, state)) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Player body did not synchronize near expected state. Expected={expected}, Actual={state}.");
    }

    public static async Task<BodySyncState> WaitForPlayerPositionNearAsync(
        GameContext context,
        int playerIndex,
        BodySyncState expected,
        float maxDistanceSquared) {
        BodySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetPlayerBodyStateAsync(context, playerIndex);
            if (expected.Exists
                && state.Exists
                && Vector3Dto.DistanceSquared(expected.Position, state.Position) <= maxDistanceSquared) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Player body did not synchronize near expected position. Expected={expected}, Actual={state}.");
    }

    public static async Task<BodySyncState> WaitForPlayerHorizontalPositionNearAsync(
        GameContext context,
        int playerIndex,
        BodySyncState expected,
        float maxDistanceSquared) {
        BodySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetPlayerBodyStateAsync(context, playerIndex);
            if (expected.Exists
                && state.Exists
                && Vector3Dto.HorizontalDistanceSquared(expected.Position, state.Position) <= maxDistanceSquared) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Player body did not synchronize near expected horizontal position. Expected={expected}, Actual={state}.");
    }

    public static bool BodyStatesNear(BodySyncState expected, BodySyncState actual) =>
        expected.Exists
        && actual.Exists
        && expected.Id == actual.Id
        && Vector3Dto.DistanceSquared(expected.Position, actual.Position) <= PositionEpsilonSquared
        && Math.Abs(expected.Velocity.X - actual.Velocity.X) <= VelocityEpsilon
        && Math.Abs(expected.Velocity.Y - actual.Velocity.Y) <= VelocityEpsilon
        && Math.Abs(expected.Velocity.Z - actual.Velocity.Z) <= VelocityEpsilon;

    public static bool PlayerBodyStatesNear(BodySyncState expected, BodySyncState actual) =>
        expected.Exists
        && actual.Exists
        && Vector3Dto.DistanceSquared(expected.Position, actual.Position) <= PositionEpsilonSquared
        && Math.Abs(expected.Velocity.X - actual.Velocity.X) <= VelocityEpsilon
        && Math.Abs(expected.Velocity.Y - actual.Velocity.Y) <= VelocityEpsilon
        && Math.Abs(expected.Velocity.Z - actual.Velocity.Z) <= VelocityEpsilon;

    public static bool PlayerBodyPositionsNear(BodySyncState expected, BodySyncState actual) =>
        expected.Exists
        && actual.Exists
        && Vector3Dto.DistanceSquared(expected.Position, actual.Position) <= PositionEpsilonSquared;

    public static bool PlayerBodyHorizontalPositionsNear(BodySyncState expected, BodySyncState actual) =>
        expected.Exists
        && actual.Exists
        && Vector3Dto.HorizontalDistanceSquared(expected.Position, actual.Position) <= PlayerPositionEpsilonSquared;

    public static float HorizontalSpeed(Vector3Dto vector) =>
        MathF.Sqrt(vector.X * vector.X + vector.Z * vector.Z);

    public static async Task<BodySyncState> OffsetBodyPositionAsync(
        GameContext context,
        int entityId,
        Vector3Dto offset,
        Vector3Dto velocity) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("CreatureSync/OffsetBodyPositionCode.cs")
                .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_X__", offset.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Y__", offset.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Z__", offset.Z.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_X__", velocity.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_Y__", velocity.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_Z__", velocity.Z.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseBodyState(raw);
    }

    public static async Task<BodySyncState> OffsetMainPlayerBodyPositionAsync(
        GameContext context,
        Vector3Dto offset,
        Vector3Dto velocity) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("CreatureSync/OffsetMainPlayerBodyPositionCode.cs")
                .Replace("__OFFSET_X__", offset.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Y__", offset.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__OFFSET_Z__", offset.Z.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_X__", velocity.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_Y__", velocity.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__VELOCITY_Z__", velocity.Z.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseBodyState(raw);
    }

    public static BodySyncState ParseBodyState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new BodySyncState(false, 0, default, default, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new BodySyncState(
            true,
            ParseInt(values, "id"),
            ParseVector3(values["pos"]),
            ParseVector3(values["vel"]),
            value);
    }

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    private static Vector3Dto ParseVector3(string value) {
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
            return new Vector3Dto(x, y, z);
        }

        throw new InvalidOperationException($"Invalid Vector3 value: '{value}'.");
    }
}

public enum CreatureSoundType {
    Idle = 0,
    Pain = 1,
    Moan = 2,
    Sneeze = 3,
    Cough = 4,
    Puke = 5,
    Attack = 6
}

public record struct CreatureSoundState(
    CreatureSoundType SoundType,
    bool Played,
    double LastSoundTime,
    double LastCoughingSoundTime,
    double LastPukeSoundTime) {
    public string Raw { get; set; } = string.Empty;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Type={SoundType}, Played={Played}, Last={LastSoundTime:R}, Cough={LastCoughingSoundTime:R}, Puke={LastPukeSoundTime:R}, Raw='{Raw}')");
}

public record struct BehaviorSyncState(
    bool ChaseIsAttack,
    bool DigInMudIsDigIn,
    bool EatPickableIsFeed,
    bool FishOutOfWaterIsBend,
    bool LookAroundIsLookRandom,
    bool RandomFeedIsFeed,
    bool RandomPeckIsFeed,
    float StubbornSteedHeadShakeOrder) {
    public string Raw { get; set; } = string.Empty;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Chase={ChaseIsAttack}, DigInMud={DigInMudIsDigIn}, EatPickable={EatPickableIsFeed}, FishOutOfWater={FishOutOfWaterIsBend}, LookAround={LookAroundIsLookRandom}, RandomFeed={RandomFeedIsFeed}, RandomPeck={RandomPeckIsFeed}, HeadShake={StubbornSteedHeadShakeOrder:R}, Raw='{Raw}')");
}

public readonly record struct BodySyncState(
    bool Exists,
    int Id,
    Vector3Dto Position,
    Vector3Dto Velocity,
    string Raw) {
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Exists={Exists}, Id={Id}, Position={Position}, Velocity={Velocity}, Raw='{Raw}')");
}
