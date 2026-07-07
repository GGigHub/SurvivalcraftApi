using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

public static class EntitySyncTools {
    public const float PositionEpsilonSquared = 0.25f;
    public const float HitpointsEpsilon = 0.001f;

    public static async Task<int> CreateBoatAsync(GameContext context, float xOffset, float yOffset, float zOffset) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EntitySync/CreateStableBoatCode.cs")
                .Replace("__X_OFFSET__", xOffset.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y_OFFSET__", yOffset.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z_OFFSET__", zOffset.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<int> CreateAndRemoveBoatImmediatelyAsync(GameContext context, float xOffset, float yOffset, float zOffset) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EntitySync/CreateAndRemoveBoatImmediatelyCode.cs")
                .Replace("__X_OFFSET__", xOffset.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y_OFFSET__", yOffset.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z_OFFSET__", zOffset.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<int> RemoveEntityAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EntitySync/RemoveEntityCode.cs")
                .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<EntitySyncState> GetEntityStateAsync(GameContext context, int entityId) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EntitySync/GetEntityStateCode.cs")
                .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseEntityState(raw);
    }

    public static async Task<EntitySyncState> WaitForEntityStateAsync(GameContext context, int entityId) {
        EntitySyncState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetEntityStateAsync(context, entityId);
            if (state.Exists) {
                return state;
            }
            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<bool> WaitForEntityMissingAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            EntitySyncState state = await GetEntityStateAsync(context, entityId);
            if (!state.Exists) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    public static async Task<bool> EntityStaysMissingAsync(GameContext context, int entityId, int probes = 20) {
        for (int i = 0; i < probes; i++) {
            EntitySyncState state = await GetEntityStateAsync(context, entityId);
            if (state.Exists) {
                return false;
            }
            await Task.Delay(100);
        }

        return true;
    }

    public static async Task<bool> EntityStaysMissingOrNotBoatAsync(GameContext context, int entityId, int probes = 20) {
        for (int i = 0; i < probes; i++) {
            EntitySyncState state = await GetEntityStateAsync(context, entityId);
            if (state.Exists && state.HasBoat) {
                return false;
            }
            await Task.Delay(100);
        }

        return true;
    }

    public static async Task<(EntitySyncState ServerState, EntitySyncState ClientState)> WaitForEntityStatesMatchAsync(
        GameContext server,
        GameContext client,
        int entityId) {
        EntitySyncState serverState = default;
        EntitySyncState clientState = default;
        for (int i = 0; i < 50; i++) {
            serverState = await GetEntityStateAsync(server, entityId);
            clientState = await GetEntityStateAsync(client, entityId);
            if (StatesMatch(serverState, clientState)) {
                return (serverState, clientState);
            }
            await Task.Delay(100);
        }

        return (serverState, clientState);
    }

    public static bool StatesMatch(EntitySyncState expected, EntitySyncState actual) =>
        expected.Exists
        && actual.Exists
        && expected.Id == actual.Id
        && expected.HasBoat == actual.HasBoat
        && expected.HasBody == actual.HasBody
        && expected.HasDamage == actual.HasDamage
        && Math.Abs(expected.Hitpoints - actual.Hitpoints) <= HitpointsEpsilon
        && (!expected.HasBody
            || Vector3Dto.DistanceSquared(expected.Position, actual.Position) <= PositionEpsilonSquared);

    public static EntitySyncState ParseEntityState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new EntitySyncState(false, 0, false, false, false, default, 0f, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new EntitySyncState(
            true,
            ParseInt(values, "id"),
            ParseBool(values, "boat"),
            ParseBool(values, "body"),
            ParseBool(values, "damage"),
            ParseVector3(values["pos"]),
            ParseFloat(values, "hitpoints"),
            value);
    }

    private static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && value == "1";

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    private static float ParseFloat(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;

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

public readonly record struct EntitySyncState(
    bool Exists,
    int Id,
    bool HasBoat,
    bool HasBody,
    bool HasDamage,
    Vector3Dto Position,
    float Hitpoints,
    string Raw) {
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Exists={Exists}, Id={Id}, HasBoat={HasBoat}, HasBody={HasBody}, HasDamage={HasDamage}, Position={Position}, Hitpoints={Hitpoints:R}, Raw='{Raw}')");
}
