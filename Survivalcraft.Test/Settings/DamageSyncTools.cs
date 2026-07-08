using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class DamageSyncTools {
    public const float HitpointsEpsilon = 0.001f;
    public const float DensityEpsilon = 0.001f;
    public static async Task ConnectTestServerAsync(GameContext context, int serverPort) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await context.WaitLogAsync("Entered screen \"Player\"", 60);
        Assert.True(playerScreen, $"Failed to load the Player screen. Recent logs:{Environment.NewLine}{context.GetRecentLogText()}");

        await context.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await WaitForClientPlayerIndexAsync(context);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
    }

    public static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
            int playerIndex = result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            if (playerIndex >= 0) {
                return playerIndex;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException("Client player index was not assigned.");
    }

    public static async Task<int> EnsureServerPlayerEntityAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("DamageSync/EnsureServerPlayerEntityCode.cs").Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<bool> WaitForPlayerInventoryAsync(GameContext context, int playerIndex) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync(
                "FUNC " + GameTools.ReadScript("DamageSync/WaitForPlayerInventoryCode.cs").Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture)));
            if (result is bool exists && exists) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    public static async Task<ToolSlotState> GiveAndDamageActiveToolAsync(GameContext context, int playerIndex, int damageCount) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("DamageSync/GiveAndDamageActiveToolCode.cs").Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture)).Replace("__DAMAGE_COUNT__", damageCount.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseToolSlotState(raw);
    }

    public static async Task<ToolSlotState> GiveAndBreakActiveToolAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("DamageSync/GiveAndBreakActiveToolCode.cs").Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseToolSlotState(raw);
    }

    public static async Task<ToolSlotState> WaitForToolSlotStateAsync(GameContext context, int playerIndex, ToolSlotState expected) {
        ToolSlotState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetToolSlotStateAsync(context, playerIndex);
            if (state.Exists
                && state.Value == expected.Value
                && state.Count == expected.Count
                && state.Damage == expected.Damage) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Tool slot did not synchronize for player {playerIndex}. Expected={expected}, Actual={state}.");
    }

    public static async Task<ToolSlotState> GetToolSlotStateAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("DamageSync/GetToolSlotStateCode.cs").Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseToolSlotState(raw);
    }

    public static ToolSlotState ParseToolSlotState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new ToolSlotState(false, 0, 0, 0, -1, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new ToolSlotState(
            true,
            ParseInt(values, "value"),
            ParseInt(values, "count"),
            ParseInt(values, "contents"),
            ParseInt(values, "damage"),
            value);
    }

    public static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    public readonly record struct ToolSlotState(
        bool Exists,
        int Value,
        int Count,
        int Contents,
        int Damage,
        string Raw);

    public static async Task<int> CreateBoatAsync(GameContext context, float xOffset, float yOffset, float zOffset) {
        object? result = await context.PushCommandAsync("FUNC " + GameTools.ReadScript("DamageSync/CreateBoatCode.cs").Replace("{0}", xOffset.ToString(CultureInfo.InvariantCulture)).Replace("{1}", yOffset.ToString(CultureInfo.InvariantCulture)).Replace("{2}", zOffset.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<DamageEntityState> DamageBoatAsync(GameContext context, int entityId, float amount) {
        string raw = (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("DamageSync/DamageBoatCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)).Replace("{1}", amount.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseDamageEntityState(raw);
    }

    public static async Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync($"FUNC return Game.GameManager.Project.FindEntity({entityId}) != null;");
            if (result is bool exists && exists) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    public static async Task<bool> WaitForEntityMissingAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync($"FUNC return Game.GameManager.Project.FindEntity({entityId}) == null;");
            if (result is bool missing && missing) {
                return true;
            }
            await Task.Delay(100);
        }

        return false;
    }

    public static async Task<DamageEntityState> WaitForDamageEntityStateAsync(GameContext context, int entityId, float expectedHitpoints) {
        DamageEntityState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetDamageEntityStateAsync(context, entityId);
            if (state.Exists && Math.Abs(state.Hitpoints - expectedHitpoints) <= HitpointsEpsilon) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"ComponentDamage hitpoints did not synchronize for entity {entityId}. Expected={expectedHitpoints}, Actual={state}.");
    }

    public static async Task<DamageEntityState> WaitForBoatDensityAsync(GameContext context, int entityId, float expectedDensity) {
        DamageEntityState state = default;
        for (int i = 0; i < 50; i++) {
            state = await UpdateAndGetDamageEntityStateAsync(context, entityId);
            if (state.Exists && Math.Abs(state.Density - expectedDensity) <= DensityEpsilon) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Boat density did not reach {expectedDensity} for entity {entityId}. Actual={state}.");
    }

    public static async Task<DamageEntityState> WaitForHitpointsAtMostAsync(GameContext context, int entityId, float expectedMaxHitpoints) {
        DamageEntityState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetDamageEntityStateAsync(context, entityId);
            if (state.Exists && state.Hitpoints <= expectedMaxHitpoints + HitpointsEpsilon) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"ComponentDamage hitpoints did not fall to {expectedMaxHitpoints} or lower for entity {entityId}. Actual={state}.");
    }

    public static async Task<DamageEntityState> GetDamageEntityStateAsync(GameContext context, int entityId) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("DamageSync/GetDamageEntityStateCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseDamageEntityState(raw);
    }

    public static async Task<DamageEntityState> UpdateAndGetDamageEntityStateAsync(GameContext context, int entityId) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("DamageSync/UpdateAndGetDamageEntityStateCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseDamageEntityState(raw);
    }

    public static bool DamageStatesMatch(DamageEntityState expected, DamageEntityState actual) =>
        expected.Exists
        && actual.Exists
        && Math.Abs(expected.Hitpoints - actual.Hitpoints) <= HitpointsEpsilon;

    public static DamageEntityState ParseDamageEntityState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new DamageEntityState(false, 0f, 0f, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new DamageEntityState(
            true,
            ParseFloat(values, "hitpoints"),
            ParseFloat(values, "density"),
            value);
    }

    public static float ParseFloat(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;

    public readonly record struct DamageEntityState(
        bool Exists,
        float Hitpoints,
        float Density,
        string Raw);
}
