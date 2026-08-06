using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class InventorySyncTools {
    public const float HeatLevelEpsilon = 0.001f;
    public const float FireTimeEpsilon = 3f;
    public const float SmeltingProgressEpsilon = 0.08f;

    public static async Task<ChestTarget> CreateChestAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/CreateChestCode.cs")) ?? "missing";

        return ParseChestTarget(raw);
    }

    public static async Task<int> CreateFurnaceAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/CreateFurnaceCode.cs"));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<int> ConnectTestServerAsync(GameContext context, int serverPort) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await WaitForScreenAsync(context, "PlayerScreen");
        Assert.True(playerScreen, $"Failed to load the Player screen. Recent logs:{Environment.NewLine}{context.GetRecentLogText()}");

        await context.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await WaitForClientPlayerIndexAsync(context);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
        return playerIndex;
    }

    public static async Task<int> EnsureServerPlayerEntityAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/EnsureServerPlayerEntityCode.cs").Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<bool> WaitForScreenAsync(GameContext context, string screenTypeName) {
        for (int i = 0; i < 600; i++) {
            object? result = await context.PushCommandAsync(
                "FUNC return Game.ScreensManager.CurrentScreen?.GetType().Name ?? \"\";");
            if (string.Equals(result as string, screenTypeName, StringComparison.Ordinal)) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    public static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        for (int i = 0; i < 80; i++) {
            object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
            int playerIndex = result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            if (playerIndex >= 0) {
                return playerIndex;
            }

            await Task.Delay(100);
        }

        return -1;
    }

    public static async Task<bool> WaitForMainPlayerAsync(GameContext context) {
        for (int i = 0; i < 120; i++) {
            object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/WaitForMainPlayerCode.cs"));
            if (result is bool ready && ready) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    public static async Task<bool> WaitForChestInventoryAsync(GameContext context, ChestTarget target) {
        for (int i = 0; i < 80; i++) {
            ChestTarget state = await GetChestInventoryAsync(context, target);
            if (state.Exists
                && state.HasTerrainCell
                && state.InventoryId == target.InventoryId
                && state.Slot0Value == target.Slot0Value
                && state.Slot0Count == target.Slot0Count) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    public static async Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync(
                $"FUNC return Game.GameManager.Project.FindEntity({entityId}) != null;");
            if (result is bool exists && exists) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    public static async Task StartFurnaceSmeltingAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/StartFurnaceSmeltingCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)));

        if (result as string != "started") {
            throw new InvalidOperationException($"Failed to start furnace smelting for entity {entityId}. Result={result ?? "null"}.");
        }
    }

    public static async Task<FurnaceInventoryState> GetFurnaceInventoryAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/GetFurnaceInventoryCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)));

        return ParseFurnaceInventoryState(result as string ?? string.Empty);
    }

    public static async Task SetFurnaceProgressAsync(GameContext context, int entityId, float progress) {
        string script = GameTools.ReadScript("InventorySync/SetFurnaceProgressCode.cs")
            .Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture))
            .Replace("{1}", progress.ToString("R", CultureInfo.InvariantCulture) + "f");
        object? result = await context.PushCommandAsync("FUNC " + script);
        if (result is not bool success || !success) {
            throw new InvalidOperationException($"Failed to set furnace progress for entity {entityId}. Result={result ?? "null"}.");
        }
    }

    public static async Task<FurnaceState> WaitForActiveFurnaceStateAsync(GameContext context, int entityId) {
        FurnaceState state = default;
        for (int i = 0; i < 80; i++) {
            state = await GetFurnaceStateAsync(context, entityId);
            if (state.Exists
                && state.FireTimeRemaining > 0f
                && state.HeatLevel > 0f
                && state.SmeltingProgress > 0f) {
                return state;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Furnace {entityId} did not become active on {context.RoleName}. LastState={state}.");
    }

    public static async Task<ChestTarget> GetChestInventoryAsync(GameContext context, ChestTarget target) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/GetChestInventoryCode.cs").Replace("{0}", target.X.ToString(CultureInfo.InvariantCulture)).Replace("{1}", target.Y.ToString(CultureInfo.InvariantCulture)).Replace("{2}", target.Z.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseChestTarget(raw);
    }

    public static async Task<FurnaceState> GetFurnaceStateAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/GetFurnaceStateCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)));

        return ParseFurnaceState(result as string ?? string.Empty);
    }

    public static async Task<bool> InteractWithChestAsync(GameContext context, ChestTarget target) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/InteractWithChestCode.cs").Replace("{0}", target.X.ToString(CultureInfo.InvariantCulture)).Replace("{1}", target.Y.ToString(CultureInfo.InvariantCulture)).Replace("{2}", target.Z.ToString(CultureInfo.InvariantCulture)));

        return result is bool interacted && interacted;
    }

    public static async Task<OpenChestState> WaitForOpenChestAsync(GameContext context, ChestTarget target) {
        OpenChestState state = default;
        for (int i = 0; i < 80; i++) {
            state = await GetOpenChestStateAsync(context);
            if (state.Opened
                && state.InventoryId == target.InventoryId
                && state.Slot0Value == target.Slot0Value
                && state.Slot0Count == target.Slot0Count) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<OpenChestState> GetOpenChestStateAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("InventorySync/GetOpenChestStateCode.cs")) ?? string.Empty;

        return ParseOpenChestState(raw);
    }

    public static ChestTarget ParseChestTarget(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new ChestTarget(false, 0, 0, 0, 0, 0, 0, false, value);
        }

        Dictionary<string, string> values = ParseKeyValuePairs(value);
        return new ChestTarget(
            true,
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "inventoryId"),
            ParseInt(values, "slot0Value"),
            ParseInt(values, "slot0Count"),
            ParseBool(values, "hasTerrainCell"),
            value);
    }

    public static OpenChestState ParseOpenChestState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        bool opened = values.TryGetValue("opened", out string? openedValue)
            && bool.TryParse(openedValue, out bool result)
            && result;

        return new OpenChestState(
            opened,
            values.TryGetValue("widget", out string? widget) ? widget : string.Empty,
            ParseInt(values, "inventoryId"),
            ParseInt(values, "slot0Value"),
            ParseInt(values, "slot0Count"),
            value);
    }

    public static FurnaceState ParseFurnaceState(string value) {
        if (value == "missing" || string.IsNullOrWhiteSpace(value)) {
            return new FurnaceState(false, 0f, 0f, 0f);
        }

        string[] parts = value.Split('|');
        if (parts.Length != 3) {
            throw new FormatException($"Invalid furnace state: \"{value}\".");
        }

        return new FurnaceState(
            true,
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    public static FurnaceInventoryState ParseFurnaceInventoryState(string value) {
        if (value == "missing" || string.IsNullOrWhiteSpace(value)) {
            return new FurnaceInventoryState(false, 0, 0, 0, 0);
        }

        string[] parts = value.Split('|');
        if (parts.Length != 4) {
            throw new FormatException($"Invalid furnace inventory state: \"{value}\".");
        }

        return new FurnaceInventoryState(
            true,
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            int.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    public static bool FurnaceStatesMatch(FurnaceState expected, FurnaceState actual, out string mismatch) {
        if (!actual.Exists) {
            mismatch = "Client furnace is missing.";
            return false;
        }

        if (Math.Abs(expected.HeatLevel - actual.HeatLevel) > HeatLevelEpsilon) {
            mismatch = $"HeatLevel mismatch. Expected={expected.HeatLevel}, Actual={actual.HeatLevel}.";
            return false;
        }

        if (Math.Abs(expected.FireTimeRemaining - actual.FireTimeRemaining) > FireTimeEpsilon) {
            mismatch = $"FireTimeRemaining mismatch. Expected={expected.FireTimeRemaining}, Actual={actual.FireTimeRemaining}.";
            return false;
        }

        if (Math.Abs(expected.SmeltingProgress - actual.SmeltingProgress) > SmeltingProgressEpsilon) {
            mismatch = $"SmeltingProgress mismatch. Expected={expected.SmeltingProgress}, Actual={actual.SmeltingProgress}.";
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    public static Dictionary<string, string> ParseKeyValuePairs(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

    public static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    public static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && bool.TryParse(value, out bool result)
        && result;

    public readonly record struct ChestTarget(
        bool Exists,
        int X,
        int Y,
        int Z,
        int InventoryId,
        int Slot0Value,
        int Slot0Count,
        bool HasTerrainCell,
        string Raw);

    public readonly record struct OpenChestState(
        bool Opened,
        string Widget,
        int InventoryId,
        int Slot0Value,
        int Slot0Count,
        string Raw);

    public readonly record struct FurnaceState(
        bool Exists,
        float FireTimeRemaining,
        float SmeltingProgress,
        float HeatLevel);

    public readonly record struct FurnaceInventoryState(
        bool Exists,
        int InputCount,
        int FuelCount,
        int ResultCount,
        int RemainsCount);
}
