using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

public static class SandPhysicsTools {
    public static Task ConnectTestServerAsync(GameContext context, int serverPort) =>
        GameTools.ConnectTestServer(context, serverPort);

    public static async Task<SandTarget> SetupSandColumnAsync(GameContext context) {
        string script = GameTools.ReadScript("SandPhysics/SetupSandColumnCode.cs");
        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new SandTarget(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "supportValue"),
            ParseInt(values, "sandValue"),
            raw);
    }

    public static async Task<SandTarget> SetupClientSandColumnAsync(GameContext context, int playerIndex) {
        string script = GameTools.ReadScript("SandPhysics/SetupClientSandColumnCode.cs")
            .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new SandTarget(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "supportValue"),
            ParseInt(values, "sandValue"),
            raw);
    }

    public static async Task DigSupportBlockAsync(GameContext context, SandTarget target) {
        string script = GameTools.ReadScript("SandPhysics/DigSupportBlockCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));
        await context.PushCommandAsync("FUNC " + script);
    }

    public static async Task<SandPhysicsState> GetSandPhysicsStateAsync(GameContext context, SandTarget target) {
        string script = GameTools.ReadScript("SandPhysics/GetSandPhysicsStateCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));
        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new SandPhysicsState(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "totalSets"),
            ParseInt(values, "collapsingSets"),
            ParseInt(values, "activeSets"),
            ParseInt(values, "stoppedSets"),
            ParseInt(values, "landContents"),
            ParseInt(values, "origContents"),
            ParseInt(values, "sandIndex"),
            raw);
    }

    public static async Task<SandPhysicsState> WaitForSandLandingAsync(
        GameContext context,
        SandTarget target,
        int timeoutMs = 8000) {
        SandPhysicsState state = default;
        int intervalMs = 100;
        int attempts = timeoutMs / intervalMs;
        for (int i = 0; i < attempts; i++) {
            state = await GetSandPhysicsStateAsync(context, target);
            if (state.LandContents == state.SandIndex) {
                return state;
            }
            await Task.Delay(intervalMs);
        }
        return state;
    }

    public static async Task<SandPhysicsState> WaitForSandVisibleAsync(
        GameContext context,
        SandTarget target,
        int timeoutMs = 10000) {
        SandPhysicsState state = default;
        int intervalMs = 100;
        int attempts = timeoutMs / intervalMs;
        for (int i = 0; i < attempts; i++) {
            state = await GetSandPhysicsStateAsync(context, target);
            if (state.OrigContents == state.SandIndex) {
                return state;
            }
            await Task.Delay(intervalMs);
        }
        return state;
    }

    public static async Task<bool> WaitForMainPlayerAsync(GameContext context) {
        for (int i = 0; i < 120; i++) {
            object? result = await context.PushCommandAsync(
                "FUNC return Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer)?.ComponentMiner != null;");
            if (result is bool ready && ready) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    public readonly record struct SandTarget(int X, int Y, int Z, int SupportValue, int SandValue, string Raw);

    public readonly record struct SandPhysicsState(
        int X,
        int Y,
        int Z,
        int TotalSets,
        int CollapsingSets,
        int ActiveSets,
        int StoppedSets,
        int LandContents,
        int OrigContents,
        int SandIndex,
        string Raw);
}
