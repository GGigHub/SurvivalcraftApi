using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class CreatureTools {
    public const float MovementEpsilonSquared = 0.0025f;
    public const float SyncedPositionEpsilonSquared = 0.04f;
    public const float SyncedBodyPositionEpsilonSquared = 0.09f;
    public const float SyncedBodyVelocityEpsilonSquared = 0.0004f;
    public const float SyncedBodyLookAnglesEpsilonSquared = 0.0001f;
    public const float SyncedBodyFlyOrderEpsilonSquared = 0.0001f;
    public const float SyncedBodyRotationDotEpsilon = 0.0001f;
    public static readonly bool BehaviorSyncOnly = false;

    public static string CreatureCountCode => "FUNC " + GameTools.ReadScript("CreatureSync/CreatureCountCode.cs");

    public static string ForceCreatureBehaviorCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceCreatureBehaviorCode.cs");

    public static string AddNonPathfindingBehaviorEntitiesCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/AddNonPathfindingBehaviorEntitiesCode.cs");

    public static string AddFullBehaviorSyncEntitiesCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/AddFullBehaviorSyncEntitiesCode.cs");

    public static string ForceCreatureSoundBehaviorCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceCreatureSoundBehaviorCode.cs");

    public static async Task<int> GetCreatureCount(GameContext context) =>
        (int?)await context.PushCommandAsync(CreatureCountCode) ?? -1;

    public static async Task<(int ServerCount, int ClientCount)> WaitForCreatureCountsEqual(GameContext server, GameContext client) {
        int serverCount = await GetCreatureCount(server);
        int clientCount = await GetCreatureCount(client);
        for (int i = 0; i < 100 && serverCount != clientCount; i++) {
            await Task.Delay(100);
            serverCount = await GetCreatureCount(server);
            clientCount = await GetCreatureCount(client);
        }
        return (serverCount, clientCount);
    }

    public static async Task<bool> WaitForEntityExists(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            if (await EntityExists(context, entityId)) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    public static async Task<bool> WaitForEntityMissing(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            if (!await EntityExists(context, entityId)) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    public static async Task<bool> EntityExists(GameContext context, int entityId) =>
        (bool?)await context.PushCommandAsync(
            string.Create(CultureInfo.InvariantCulture, $"FUNC Game.GameManager.Project.FindEntity({entityId}) != null")) ?? false;

    public static string BuildStopCreatureBehaviorCode(IEnumerable<int> entityIds) {
        string ids = string.Join(",", entityIds);
        return "EXEC " + GameTools.ReadScript("CreatureSync/StopCreatureBehaviorCode.cs").Replace("{0}", ids);
    }

    public static async Task<Dictionary<int, Vector3Dto>> GetCreaturePositions(GameContext context, IEnumerable<int> entityIds) {
        string positions = (string?)await context.PushCommandAsync(BuildGetCreaturePositionsCode(entityIds)) ?? string.Empty;
        Console.WriteLine($"[DEBUG-POSITIONS] Context={context.RoleName}, Positions='{positions}'");
        if (positions == "") throw new InvalidOperationException("Positions string is completely empty!");
        return ParseCreaturePositions(positions);
    }

    public static string BuildGetCreaturePositionsCode(IEnumerable<int> entityIds) {
        string ids = string.Join(",", entityIds);
        return "FUNC " + GameTools.ReadScript("CreatureSync/GetCreaturePositionsCode.cs").Replace("{0}", ids);
    }

    public static string BuildForceNonPathfindingBehaviorCode(IReadOnlyDictionary<string, int> entityIds) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceNonPathfindingBehaviorCode.cs")
            .Replace("__FISH_ID__", entityIds["FishOutOfWater"].ToString(CultureInfo.InvariantCulture))
            .Replace("__LOOK_AROUND_ID__", entityIds["LookAround"].ToString(CultureInfo.InvariantCulture))
            .Replace("__STUBBORN_STEED_ID__", entityIds["StubbornSteed"].ToString(CultureInfo.InvariantCulture));

    public static string BuildGetNonPathfindingBehaviorOrdersCode(IReadOnlyDictionary<string, int> entityIds) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/GetNonPathfindingBehaviorOrdersCode.cs")
            .Replace("__FISH_ID__", entityIds["FishOutOfWater"].ToString(CultureInfo.InvariantCulture))
            .Replace("__LOOK_AROUND_ID__", entityIds["LookAround"].ToString(CultureInfo.InvariantCulture))
            .Replace("__STUBBORN_STEED_ID__", entityIds["StubbornSteed"].ToString(CultureInfo.InvariantCulture));

    public static string BuildForceFullBehaviorSyncCode(IReadOnlyDictionary<string, int> entityIds) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceFullBehaviorSyncCode.cs")
            .Replace("__CHASE_ID__", entityIds["Chase"].ToString(CultureInfo.InvariantCulture))
            .Replace("__DIG_IN_MUD_ID__", entityIds["DigInMud"].ToString(CultureInfo.InvariantCulture))
            .Replace("__EAT_PICKABLE_ID__", entityIds["EatPickable"].ToString(CultureInfo.InvariantCulture))
            .Replace("__FISH_OUT_OF_WATER_ID__", entityIds["FishOutOfWater"].ToString(CultureInfo.InvariantCulture))
            .Replace("__HUMAN_ROW_ID__", entityIds["HumanRow"].ToString(CultureInfo.InvariantCulture))
            .Replace("__LOOK_AROUND_ID__", entityIds["LookAround"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_FEED_ID__", entityIds["RandomFeed"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_PECK_ID__", entityIds["RandomPeck"].ToString(CultureInfo.InvariantCulture))
            .Replace("__STUBBORN_STEED_ID__", entityIds["StubbornSteed"].ToString(CultureInfo.InvariantCulture));

    public static string BuildGetFullBehaviorSyncStateCode(IReadOnlyDictionary<string, int> entityIds) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/GetFullBehaviorSyncStateCode.cs")
            .Replace("__CHASE_ID__", entityIds["Chase"].ToString(CultureInfo.InvariantCulture))
            .Replace("__DIG_IN_MUD_ID__", entityIds["DigInMud"].ToString(CultureInfo.InvariantCulture))
            .Replace("__EAT_PICKABLE_ID__", entityIds["EatPickable"].ToString(CultureInfo.InvariantCulture))
            .Replace("__FISH_OUT_OF_WATER_ID__", entityIds["FishOutOfWater"].ToString(CultureInfo.InvariantCulture))
            .Replace("__HUMAN_ROW_ID__", entityIds["HumanRow"].ToString(CultureInfo.InvariantCulture))
            .Replace("__LOOK_AROUND_ID__", entityIds["LookAround"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_FEED_ID__", entityIds["RandomFeed"].ToString(CultureInfo.InvariantCulture))
            .Replace("__RANDOM_PECK_ID__", entityIds["RandomPeck"].ToString(CultureInfo.InvariantCulture))
            .Replace("__STUBBORN_STEED_ID__", entityIds["StubbornSteed"].ToString(CultureInfo.InvariantCulture));

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

    public static string BuildPrepareCreatureBodySyncCode(int entityId) =>
        "EXEC " + GameTools.ReadScript("CreatureSync/PrepareCreatureBodySyncCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));

    public static string BuildForceCreatureBodySyncCode(int entityId) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceCreatureBodySyncCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));

    public static string BuildGetCreatureBodySyncStateCode(int entityId) =>
        "FUNC " + GameTools.ReadScript("CreatureSync/GetCreatureBodySyncStateCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));

    public static async Task ConfigureCreatureSoundState(GameContext context, int entityId) {
        string result = (string?)await context.PushCommandAsync(BuildConfigureCreatureSoundStateCode(entityId)) ?? string.Empty;
        Assert.Equal("ok", result);
    }

    public static async Task MakeCreaturesInvulnerable(GameContext context, IEnumerable<int> entityIds) {
        string idsStr = string.Join(",", entityIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        string code = $"FUNC var entityIds = new int[] {{ {idsStr} }}; foreach (int id in entityIds) {{ var entity = Game.GameManager.Project.FindEntity(id); if (entity != null) {{ var health = entity.FindComponent<Game.ComponentHealth>(); if (health != null) {{ health.IsInvulnerable = true; }} }} }} return \"ok\";";
        string result = (string?)await context.PushCommandAsync(code) ?? string.Empty;
        Assert.Equal("ok", result);
    }

    public static async Task TriggerCreatureSound(GameContext context, int entityId, CreatureSoundType soundType) {
        string result = (string?)await context.PushCommandAsync(BuildTriggerCreatureSoundCode(entityId, soundType)) ?? string.Empty;
        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"ok:{(int)soundType}"),
            result);
    }

    public static async Task<CreatureSoundState> GetCreatureSoundState(
        GameContext context,
        int entityId,
        CreatureSoundType soundType) {
        string state = (string?)await context.PushCommandAsync(BuildGetCreatureSoundStateCode(entityId, soundType)) ?? string.Empty;
        Console.WriteLine($"[DEBUG-CREATURE-SOUND] Context={context.RoleName}, Type={soundType}, State='{state}'");
        return ParseCreatureSoundState(soundType, state);
    }

    public static async Task<CreatureBodyState> GetCreatureBodyState(GameContext context, int entityId) {
        string state = (string?)await context.PushCommandAsync(BuildGetCreatureBodySyncStateCode(entityId)) ?? string.Empty;
        Console.WriteLine($"[DEBUG-BODY-STATE] Context={context.RoleName}, State='{state}'");
        return ParseCreatureBodyState(state);
    }

    public static async Task<CreatureSoundState> WaitForCreatureSoundPlayed(
        GameContext context,
        int entityId,
        CreatureSoundType soundType) {
        CreatureSoundState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetCreatureSoundState(context, entityId, soundType);
            if (state.Played) {
                break;
            }
            await Task.Delay(100);
        }
        return state;
    }

    public static async Task<NonPathfindingBehaviorOrders> GetNonPathfindingBehaviorOrders(
        GameContext context,
        IReadOnlyDictionary<string, int> entityIds) {
        string orders = (string?)await context.PushCommandAsync(BuildGetNonPathfindingBehaviorOrdersCode(entityIds)) ?? string.Empty;
        Console.WriteLine($"[DEBUG-BEHAVIOR-ORDERS] Context={context.RoleName}, Orders='{orders}'");
        return ParseNonPathfindingBehaviorOrders(orders);
    }

    public static async Task<CreatureBehaviorSyncState> GetFullBehaviorSyncState(
        GameContext context,
        IReadOnlyDictionary<string, int> entityIds) {
        string state = (string?)await context.PushCommandAsync(BuildGetFullBehaviorSyncStateCode(entityIds)) ?? string.Empty;
        Console.WriteLine($"[DEBUG-FULL-BEHAVIOR-SYNC] Context={context.RoleName}, State='{state}'");
        return ParseFullBehaviorSyncState(state);
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

    public static NonPathfindingBehaviorOrders ParseNonPathfindingBehaviorOrders(string value) {
        NonPathfindingBehaviorOrders result = new() { Raw = value };
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] parts = item.Split(':', 3);
            if (parts.Length != 3) {
                continue;
            }
            if (parts[2].StartsWith("missing", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            Dictionary<string, string> values = parts[2]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

            bool hasBendOrder = values.TryGetValue("bend", out string? bendValue)
                && bendValue == "1";
            bool hasLookRandomOrder = values.TryGetValue("look", out string? lookValue)
                && lookValue == "1";
            float headShakeOrder = values.TryGetValue("shake", out string? shakeValue)
                && float.TryParse(shakeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedShake)
                    ? parsedShake
                    : 0f;

            switch (parts[0]) {
                case "FishOutOfWater":
                    result.FishOutOfWaterBendOrder = hasBendOrder;
                    break;
                case "LookAround":
                    result.LookAroundLookRandomOrder = hasLookRandomOrder;
                    break;
                case "StubbornSteed":
                    result.StubbornSteedHeadShakeOrder = headShakeOrder;
                    break;
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

    public static void AssertExpectedBehaviorOrders(NonPathfindingBehaviorOrders orders, string contextName) {
        Assert.True(orders.FishOutOfWaterBendOrder, $"{contextName} FishOutOfWater did not set BendOrder. Orders={orders}.");
        Assert.True(orders.LookAroundLookRandomOrder, $"{contextName} LookAround did not set LookRandomOrder. Orders={orders}.");
        Assert.True(orders.StubbornSteedHeadShakeOrder > 0f, $"{contextName} StubbornSteed did not set HeadShakeOrder. Orders={orders}.");
    }

    public static bool HasExpectedBehaviorOrders(NonPathfindingBehaviorOrders orders) =>
        orders.FishOutOfWaterBendOrder
        && orders.LookAroundLookRandomOrder
        && orders.StubbornSteedHeadShakeOrder > 0f;

    public static CreatureBehaviorSyncState ParseFullBehaviorSyncState(string value) {
        CreatureBehaviorSyncState result = new() { Raw = value };
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
                case "HumanRow":
                    result.HumanRowLeft = HasFlag("left");
                    result.HumanRowRight = HasFlag("right");
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

    public static void AssertExpectedFullBehaviorSyncState(CreatureBehaviorSyncState state, string contextName) {
        Assert.True(state.ChaseIsAttack, $"{contextName} Chase did not set IsAttack. State={state}.");
        Assert.True(state.DigInMudIsDigIn, $"{contextName} DigInMud did not set IsDigIn. State={state}.");
        Assert.True(state.EatPickableIsFeed, $"{contextName} EatPickable did not set IsFeed. State={state}.");
        Assert.True(state.FishOutOfWaterIsBend, $"{contextName} FishOutOfWater did not set IsBend. State={state}.");
        Assert.True(state.HumanRowLeft && state.HumanRowRight, $"{contextName} HumanRow did not set row orders. State={state}.");
        Assert.True(state.LookAroundIsLookRandom, $"{contextName} LookAround did not set IsLookRandom. State={state}.");
        Assert.True(state.RandomFeedIsFeed, $"{contextName} RandomFeed did not set IsFeed. State={state}.");
        Assert.True(state.RandomPeckIsFeed, $"{contextName} RandomPeck did not set IsFeed. State={state}.");
        Assert.True(state.StubbornSteedHeadShakeOrder > 0f, $"{contextName} StubbornSteed did not set HeadShakeOrder. State={state}.");
    }

    public static bool HasExpectedFullBehaviorSyncState(CreatureBehaviorSyncState state) =>
        state.ChaseIsAttack
        && state.DigInMudIsDigIn
        && state.EatPickableIsFeed
        && state.FishOutOfWaterIsBend
        && state.HumanRowLeft
        && state.HumanRowRight
        && state.LookAroundIsLookRandom
        && state.RandomFeedIsFeed
        && state.RandomPeckIsFeed
        && state.StubbornSteedHeadShakeOrder > 0f;

    public static CreatureBehaviorSyncState MergeFullBehaviorSyncStates(
        CreatureBehaviorSyncState state1,
        CreatureBehaviorSyncState state2) =>
        new(
            state1.ChaseIsAttack || state2.ChaseIsAttack,
            state1.DigInMudIsDigIn || state2.DigInMudIsDigIn,
            state1.EatPickableIsFeed || state2.EatPickableIsFeed,
            state1.FishOutOfWaterIsBend || state2.FishOutOfWaterIsBend,
            state1.HumanRowLeft || state2.HumanRowLeft,
            state1.HumanRowRight || state2.HumanRowRight,
            state1.LookAroundIsLookRandom || state2.LookAroundIsLookRandom,
            state1.RandomFeedIsFeed || state2.RandomFeedIsFeed,
            state1.RandomPeckIsFeed || state2.RandomPeckIsFeed,
            MathF.Max(state1.StubbornSteedHeadShakeOrder, state2.StubbornSteedHeadShakeOrder)) {
            Raw = state2.Raw
        };

    public static CreatureBodyState ParseCreatureBodyState(string value) {
        string[] idAndData = value.Split('|', 2);
        if (idAndData.Length != 2
            || !int.TryParse(idAndData[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entityId)) {
            throw new InvalidOperationException($"Invalid creature body state: '{value}'.");
        }
        if (idAndData[1].StartsWith("missing", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Creature {entityId} body state is missing. Reason: {idAndData[1]}");
        }

        Dictionary<string, string> values = idAndData[1]
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new CreatureBodyState(
            entityId,
            ParseVector3(values, "pos"),
            ParseQuaternion(values, "rot"),
            ParseVector3(values, "vel"),
            ParseVector2(values, "look"),
            values.TryGetValue("fly", out string? flyValue) && !flyValue.Equals("null", StringComparison.OrdinalIgnoreCase)
                ? ParseVector3(flyValue)
                : null) {
            Raw = value
        };
    }

    public static bool HasBodyInterventionApplied(CreatureBodyState before, CreatureBodyState after) =>
        Vector3Dto.DistanceSquared(before.Position, after.Position) > MovementEpsilonSquared
        && QuaternionDto.RotationDifference(after.Rotation, before.Rotation) > SyncedBodyRotationDotEpsilon
        && Vector3Dto.DistanceSquared(before.Velocity, after.Velocity) > SyncedBodyVelocityEpsilonSquared
        && Vector2Dto.DistanceSquared(before.LookAngles, after.LookAngles) > SyncedBodyLookAnglesEpsilonSquared
        && after.FlyOrder.HasValue;

    public static bool BodyStatesMatch(CreatureBodyState expected, CreatureBodyState actual, out string mismatch) {
        if (expected.EntityId != actual.EntityId) {
            mismatch = string.Create(CultureInfo.InvariantCulture, $"EntityId expected {expected.EntityId}, got {actual.EntityId}");
            return false;
        }
        if (Vector3Dto.DistanceSquared(expected.Position, actual.Position) > SyncedBodyPositionEpsilonSquared) {
            mismatch = string.Create(CultureInfo.InvariantCulture, $"Position expected {expected.Position}, got {actual.Position}");
            return false;
        }
        if (QuaternionDto.RotationDifference(expected.Rotation, actual.Rotation) > SyncedBodyRotationDotEpsilon) {
            mismatch = string.Create(CultureInfo.InvariantCulture, $"Rotation expected {expected.Rotation}, got {actual.Rotation}");
            return false;
        }
        if (Vector3Dto.DistanceSquared(expected.Velocity, actual.Velocity) > SyncedBodyVelocityEpsilonSquared) {
            mismatch = string.Create(CultureInfo.InvariantCulture, $"Velocity expected {expected.Velocity}, got {actual.Velocity}");
            return false;
        }
        if (Vector2Dto.DistanceSquared(expected.LookAngles, actual.LookAngles) > SyncedBodyLookAnglesEpsilonSquared) {
            mismatch = string.Create(CultureInfo.InvariantCulture, $"LookAngles expected {expected.LookAngles}, got {actual.LookAngles}");
            return false;
        }
        if (expected.FlyOrder.HasValue != actual.FlyOrder.HasValue) {
            mismatch = string.Create(CultureInfo.InvariantCulture, $"FlyOrder expected {expected.FlyOrder}, got {actual.FlyOrder}");
            return false;
        }
        if (expected.FlyOrder.HasValue
            && actual.FlyOrder.HasValue
            && Vector3Dto.DistanceSquared(expected.FlyOrder.Value, actual.FlyOrder.Value) > SyncedBodyFlyOrderEpsilonSquared) {
            mismatch = string.Create(CultureInfo.InvariantCulture, $"FlyOrder expected {expected.FlyOrder}, got {actual.FlyOrder}");
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    private static Vector2Dto ParseVector2(Dictionary<string, string> values, string key) {
        if (!values.TryGetValue(key, out string? value)) {
            throw new InvalidOperationException($"Creature body state is missing '{key}'.");
        }
        string[] components = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 2
            && float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
            return new Vector2Dto(x, y);
        }
        throw new InvalidOperationException($"Invalid Vector2 value for '{key}': '{value}'.");
    }

    private static Vector3Dto ParseVector3(Dictionary<string, string> values, string key) {
        if (!values.TryGetValue(key, out string? value)) {
            throw new InvalidOperationException($"Creature body state is missing '{key}'.");
        }
        return ParseVector3(value);
    }

    private static Vector3Dto ParseVector3(string value) {
        string[] components = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 3
            && float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
            return new Vector3Dto(x, y, z);
        }
        throw new InvalidOperationException($"Invalid Vector3 value: '{value}'.");
    }

    private static QuaternionDto ParseQuaternion(Dictionary<string, string> values, string key) {
        if (!values.TryGetValue(key, out string? value)) {
            throw new InvalidOperationException($"Creature body state is missing '{key}'.");
        }
        string[] components = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 4
            && float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            && float.TryParse(components[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float w)) {
            return new QuaternionDto(x, y, z, w);
        }
        throw new InvalidOperationException($"Invalid Quaternion value for '{key}': '{value}'.");
    }

    public static Dictionary<int, Vector3Dto> ParseCreaturePositions(string value) {
        Dictionary<int, Vector3Dto> result = [];
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] pair = item.Split(':', 2);
            if (pair.Length != 2
                || !int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entityId)) {
                continue;
            }
            if (pair[1].StartsWith("missing", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Creature {entityId} is missing. Reason: {pair[1]}");
            }

            string[] components = pair[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (components.Length == 3
                && float.TryParse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                && float.TryParse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
                result[entityId] = new Vector3Dto(x, y, z);
            }
        }
        return result;
    }
}

public readonly record struct Vector3Dto(float X, float Y, float Z) {
    public static float DistanceSquared(Vector3Dto v1, Vector3Dto v2) {
        float x = v1.X - v2.X;
        float y = v1.Y - v2.Y;
        float z = v1.Z - v2.Z;
        return x * x + y * y + z * z;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R}, {Z:R})");
}

public readonly record struct Vector2Dto(float X, float Y) {
    public static float DistanceSquared(Vector2Dto v1, Vector2Dto v2) {
        float x = v1.X - v2.X;
        float y = v1.Y - v2.Y;
        return x * x + y * y;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R})");
}

public readonly record struct QuaternionDto(float X, float Y, float Z, float W) {
    public static float RotationDifference(QuaternionDto q1, QuaternionDto q2) {
        float dot = Math.Abs(q1.X * q2.X + q1.Y * q2.Y + q1.Z * q2.Z + q1.W * q2.W);
        return 1f - Math.Min(dot, 1f);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R}, {Z:R}, {W:R})");
}

public record struct CreatureBodyState(
    int EntityId,
    Vector3Dto Position,
    QuaternionDto Rotation,
    Vector3Dto Velocity,
    Vector2Dto LookAngles,
    Vector3Dto? FlyOrder) {
    public string Raw { get; set; } = string.Empty;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(EntityId={EntityId}, Position={Position}, Rotation={Rotation}, Velocity={Velocity}, LookAngles={LookAngles}, FlyOrder={FlyOrder}, Raw='{Raw}')");
}

public record struct NonPathfindingBehaviorOrders(
    bool FishOutOfWaterBendOrder,
    bool LookAroundLookRandomOrder,
    float StubbornSteedHeadShakeOrder) {
    public string Raw { get; set; } = string.Empty;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Bend={FishOutOfWaterBendOrder}, LookRandom={LookAroundLookRandomOrder}, HeadShake={StubbornSteedHeadShakeOrder:R}, Raw='{Raw}')");
}

public record struct CreatureBehaviorSyncState(
    bool ChaseIsAttack,
    bool DigInMudIsDigIn,
    bool EatPickableIsFeed,
    bool FishOutOfWaterIsBend,
    bool HumanRowLeft,
    bool HumanRowRight,
    bool LookAroundIsLookRandom,
    bool RandomFeedIsFeed,
    bool RandomPeckIsFeed,
    float StubbornSteedHeadShakeOrder) {
    public string Raw { get; set; } = string.Empty;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Chase={ChaseIsAttack}, DigInMud={DigInMudIsDigIn}, EatPickable={EatPickableIsFeed}, FishOutOfWater={FishOutOfWaterIsBend}, HumanRowLeft={HumanRowLeft}, HumanRowRight={HumanRowRight}, LookAround={LookAroundIsLookRandom}, RandomFeed={RandomFeedIsFeed}, RandomPeck={RandomPeckIsFeed}, HeadShake={StubbornSteedHeadShakeOrder:R}, Raw='{Raw}')");
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
