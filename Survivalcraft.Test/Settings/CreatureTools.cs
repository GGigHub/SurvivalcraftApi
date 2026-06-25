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
    public const bool BehaviorSyncOnly = false;

    public static string CreatureCountCode => "FUNC " + GameTools.ReadScript("CreatureSync/CreatureCountCode.cs");

    public static string ForceCreatureBehaviorCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/ForceCreatureBehaviorCode.cs");

    public static string AddNonPathfindingBehaviorEntitiesCode =>
        "FUNC " + GameTools.ReadScript("CreatureSync/AddNonPathfindingBehaviorEntitiesCode.cs");

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

    public static async Task<NonPathfindingBehaviorOrders> GetNonPathfindingBehaviorOrders(
        GameContext context,
        IReadOnlyDictionary<string, int> entityIds) {
        string orders = (string?)await context.PushCommandAsync(BuildGetNonPathfindingBehaviorOrdersCode(entityIds)) ?? string.Empty;
        Console.WriteLine($"[DEBUG-BEHAVIOR-ORDERS] Context={context.RoleName}, Orders='{orders}'");
        return ParseNonPathfindingBehaviorOrders(orders);
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

    public static void AssertExpectedBehaviorOrders(NonPathfindingBehaviorOrders orders, string contextName) {
        Assert.True(orders.FishOutOfWaterBendOrder, $"{contextName} FishOutOfWater did not set BendOrder. Orders={orders}.");
        Assert.True(orders.LookAroundLookRandomOrder, $"{contextName} LookAround did not set LookRandomOrder. Orders={orders}.");
        Assert.True(orders.StubbornSteedHeadShakeOrder > 0f, $"{contextName} StubbornSteed did not set HeadShakeOrder. Orders={orders}.");
    }

    public static bool HasExpectedBehaviorOrders(NonPathfindingBehaviorOrders orders) =>
        orders.FishOutOfWaterBendOrder
        && orders.LookAroundLookRandomOrder
        && orders.StubbornSteedHeadShakeOrder > 0f;

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
