using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class EnvironmentSyncTools {
    public const double TimeTolerance = 2d;
    public const double DoubleTolerance = 0.001d;
    public const float FloatTolerance = 0.001f;

    public static Task CreateCreativeTestWorldAsync(GameContext server, string seed) =>
        PlayerSyncTools.CreateCreativeTestWorldAsync(server, seed);

    public static async Task ConnectTestServerAsync(GameContext client, int serverPort) {
        await client.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs")
                .Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 60);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"", 60);
        Assert.True(playerScreen, $"Failed to load the Player screen. Recent logs:{Environment.NewLine}{client.GetRecentLogText()}");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
    }

    public static async Task<bool> WaitForMainPlayerAsync(GameContext context) {
        for (int i = 0; i < 80; i++) {
            object? result = await context.PushCommandAsync("FUNC " + GameTools.ReadScript("InventorySync/WaitForMainPlayerCode.cs"));
            if (result is bool ready && ready) {
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

        throw new TimeoutException("Client player index was not assigned.");
    }

    public static async Task<WorldEnvironmentState> GetStateAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("EnvironmentSync/GetWorldEnvironmentStateCode.cs")) ?? "missing";
        return ParseState(raw);
    }

    public static async Task<WorldEnvironmentState> ForcePrecipitationRescheduleAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EnvironmentSync/ForcePrecipitationRescheduleCode.cs")) ?? "missing";
        return ParseState(raw);
    }

    public static async Task<WorldEnvironmentState> ForceFogRescheduleAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EnvironmentSync/ForceFogRescheduleCode.cs")) ?? "missing";
        return ParseState(raw);
    }

    public static async Task<WorldEnvironmentState> SetTimeOfYearAsync(GameContext context, float timeOfYear) {
        string script = GameTools.ReadScript("EnvironmentSync/SetTimeOfYearCode.cs")
            .Replace("__TIME_OF_YEAR__", timeOfYear.ToString(CultureInfo.InvariantCulture) + "f");
        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? "missing";
        return ParseState(raw);
    }

    public static async Task<string> TriggerManualEnvironmentInputAsync(GameContext context, ManualEnvironmentInput input) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("EnvironmentSync/TriggerManualEnvironmentInputCode.cs")
                .Replace("__ACTION__", input.ToString())) ?? string.Empty;
        Assert.Equal(input.ToString(), raw);
        return raw;
    }

    public static Task<WorldEnvironmentState> WaitForServerTimeAtLeastAsync(GameContext context, double expectedTime) =>
        WaitForStateAsync(
            context,
            state => state.Exists && state.TotalElapsedGameTime >= expectedTime,
            $"World time did not reach {expectedTime.ToString(CultureInfo.InvariantCulture)}.");

    public static Task<WorldEnvironmentState> WaitForTimeCloseAsync(GameContext context, WorldEnvironmentState expected) =>
        WaitForStateAsync(
            context,
            state => state.Exists && Math.Abs(state.TotalElapsedGameTime - expected.TotalElapsedGameTime) <= TimeTolerance,
            $"World time did not approach server time {expected.Raw}.");

    public static Task<WorldEnvironmentState> WaitForPrecipitationScheduleAsync(GameContext context) =>
        WaitForStateAsync(
            context,
            state => state.Exists && state.PrecipitationStartTime >= 0d && state.PrecipitationEndTime > state.PrecipitationStartTime,
            "Precipitation schedule was not regenerated.");

    public static Task<WorldEnvironmentState> WaitForPrecipitationScheduleAsync(GameContext context, WorldEnvironmentState expected) =>
        WaitForStateAsync(
            context,
            state => PrecipitationScheduleMatches(expected, state),
            $"Precipitation schedule did not match {expected.Raw}.");

    public static Task<WorldEnvironmentState> WaitForFogScheduleAsync(GameContext context) =>
        WaitForStateAsync(
            context,
            state => state.Exists && state.FogStartTime >= 0d && state.FogEndTime > state.FogStartTime && state.FogRampTime > 0f,
            "Fog schedule was not regenerated.");

    public static Task<WorldEnvironmentState> WaitForFogScheduleAsync(GameContext context, WorldEnvironmentState expected) =>
        WaitForStateAsync(
            context,
            state => FogScheduleMatches(expected, state),
            $"Fog schedule did not match {expected.Raw}.");

    public static Task<WorldEnvironmentState> WaitForTimeOfYearAsync(GameContext context, float expectedTimeOfYear) =>
        WaitForStateAsync(
            context,
            state => state.Exists && Close(state.TimeOfYear, expectedTimeOfYear, FloatTolerance),
            $"WorldSettings.TimeOfYear did not reach {expectedTimeOfYear.ToString(CultureInfo.InvariantCulture)}.");

    public static Task<WorldEnvironmentState> WaitForTimeOfDayOffsetAsync(GameContext context, double expectedOffset) =>
        WaitForStateAsync(
            context,
            state => state.Exists && Close(state.TimeOfDayOffset, expectedOffset, DoubleTolerance),
            $"TimeOfDayOffset did not reach {expectedOffset.ToString(CultureInfo.InvariantCulture)}.");

    public static Task<WorldEnvironmentState> WaitForPrecipitationStartedAsync(GameContext context, bool expected) =>
        WaitForStateAsync(
            context,
            state => state.Exists && state.IsPrecipitationStarted == expected,
            $"Precipitation started state did not become {expected}.");

    public static Task<WorldEnvironmentState> WaitForFogStartedAsync(GameContext context, bool expected) =>
        WaitForStateAsync(
            context,
            state => state.Exists && state.IsFogStarted == expected,
            $"Fog started state did not become {expected}.");

    public static Task<WorldEnvironmentState> WaitForLightningAfterAsync(GameContext context, double previousLightningTime) =>
        WaitForStateAsync(
            context,
            state => state.Exists && state.LastLightningStrikeTime > previousLightningTime + DoubleTolerance,
            $"Lightning strike time did not advance past {previousLightningTime.ToString(CultureInfo.InvariantCulture)}.");

    public static Task<WorldEnvironmentState> WaitForStateChangeAsync(
        GameContext context,
        Func<WorldEnvironmentState, bool> predicate,
        string failureMessage) =>
        WaitForStateAsync(context, state => state.Exists && predicate(state), failureMessage);

    public static bool PrecipitationScheduleMatches(WorldEnvironmentState expected, WorldEnvironmentState actual) =>
        actual.Exists
        && Close(expected.PrecipitationStartTime, actual.PrecipitationStartTime, DoubleTolerance)
        && Close(expected.PrecipitationEndTime, actual.PrecipitationEndTime, DoubleTolerance)
        && Close(expected.LightningIntensity, actual.LightningIntensity, FloatTolerance);

    public static bool FogScheduleMatches(WorldEnvironmentState expected, WorldEnvironmentState actual) =>
        actual.Exists
        && Close(expected.FogStartTime, actual.FogStartTime, DoubleTolerance)
        && Close(expected.FogEndTime, actual.FogEndTime, DoubleTolerance)
        && Close(expected.FogRampTime, actual.FogRampTime, FloatTolerance);

    public static bool Close(double actual, double expected, double tolerance) =>
        Math.Abs(actual - expected) <= tolerance;

    public static bool Close(float actual, float expected, float tolerance) =>
        Math.Abs(actual - expected) <= tolerance;

    private static async Task<WorldEnvironmentState> WaitForStateAsync(
        GameContext context,
        Func<WorldEnvironmentState, bool> predicate,
        string failureMessage) {
        WorldEnvironmentState state = default;
        for (int i = 0; i < 120; i++) {
            state = await GetStateAsync(context);
            if (predicate(state)) {
                return state;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"{failureMessage} Actual={state.Raw}");
    }

    private static WorldEnvironmentState ParseState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new(false, 0d, 0d, 0f, 0d, 0d, 0f, 0d, 0d, 0f, false, false, 0d, float.NaN, float.NaN, float.NaN, 0f, raw);
        }

        Dictionary<string, string> map = raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new(
            true,
            Double(map, "time"),
            Double(map, "offset"),
            Float(map, "timeOfYear"),
            Double(map, "precipStart"),
            Double(map, "precipEnd"),
            Float(map, "lightning"),
            Double(map, "fogStart"),
            Double(map, "fogEnd"),
            Float(map, "fogRamp"),
            Bool(map, "precipStarted"),
            Bool(map, "fogStarted"),
            Double(map, "lastLightning"),
            Float(map, "lightningX"),
            Float(map, "lightningY"),
            Float(map, "lightningZ"),
            Float(map, "lightningBrightness"),
            raw);
    }

    private static double Double(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result
            : 0d;

    private static float Float(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;

    private static bool Bool(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && bool.TryParse(value, out bool result) && result;

    public enum ManualEnvironmentInput {
        TimeOfDay,
        Lightning,
        Precipitation,
        Fog
    }

    public readonly record struct WorldEnvironmentState(
        bool Exists,
        double TotalElapsedGameTime,
        double TimeOfDayOffset,
        float TimeOfYear,
        double PrecipitationStartTime,
        double PrecipitationEndTime,
        float LightningIntensity,
        double FogStartTime,
        double FogEndTime,
        float FogRampTime,
        bool IsPrecipitationStarted,
        bool IsFogStarted,
        double LastLightningStrikeTime,
        float LightningStrikeX,
        float LightningStrikeY,
        float LightningStrikeZ,
        float LightningStrikeBrightness,
        string Raw);
}
