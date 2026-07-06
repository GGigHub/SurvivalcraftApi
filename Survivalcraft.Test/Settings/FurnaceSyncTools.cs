using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class FurnaceSyncTools {
    public const float HeatLevelEpsilon = 0.001f;
    public const float FireTimeEpsilon = 3f;
    public const float SmeltingProgressEpsilon = 0.08f;
    public static async Task<int> CreateFurnaceAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("FurnaceSync/CreateFurnaceCode.cs"));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task ConnectTestServerAsync(GameContext context, int serverPort) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await context.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await WaitForScreenAsync(context, "PlayerScreen");
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

    public static async Task StartFurnaceSmeltingAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("FurnaceSync/StartFurnaceSmeltingCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)));

        if (result as string != "started") {
            throw new InvalidOperationException($"Failed to start furnace smelting for entity {entityId}. Result={result ?? "null"}.");
        }
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

    public static async Task<FurnaceState> GetFurnaceStateAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("FurnaceSync/GetFurnaceStateCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)));

        return ParseFurnaceState(result as string ?? string.Empty);
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

    public readonly record struct FurnaceState(
        bool Exists,
        float FireTimeRemaining,
        float SmeltingProgress,
        float HeatLevel);
}
