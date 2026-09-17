using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

internal static class ElytronSyncTools
{
    private const int FireworksBlockId = 215;
    private const int ElytraClothingIndex = 322;

    public static async Task<string> GetInstallStateAsync(GameContext context, int playerIndex)
    {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Elytron/GetElytronInstallStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "no-response";
    }

    public static async Task<string> EquipElytraAsync(GameContext context, int playerIndex)
    {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Elytron/EquipElytraOnServerCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "no-response";
    }

    public static async Task<string> GetEquipmentFingerprintAsync(GameContext context, int playerIndex)
    {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Elytron/GetEquipmentFingerprintCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "no-response";
    }

    public static async Task<string> TeleportPlayerUpAsync(GameContext context, int playerIndex, float rise)
    {
        string script = GameTools.ReadScript("Elytron/TeleportPlayerUpCode.cs");
        script = ReplaceInvariant(script, "__PLAYER_INDEX__", playerIndex);
        script = script.Replace("__RISE__", rise.ToString("R", CultureInfo.InvariantCulture));
        return (string?)await context.PushCommandAsync("FUNC " + script) ?? "no-response";
    }

    public static async Task<string> InjectDoubleJumpAsync(GameContext context, int playerIndex)
    {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Elytron/InjectDoubleJumpOnClientCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "no-response";
    }

    public static async Task<string> GetGlideFingerprintAsync(GameContext context, int playerIndex)
    {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Elytron/GetGlideFingerprintCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "no-response";
    }

    public static async Task<string> SeedFireworkInActiveSlotAsync(GameContext context, int playerIndex, int count)
    {
        string script = GameTools.ReadScript("Elytron/SeedFireworkInActiveSlotCode.cs");
        script = ReplaceInvariant(script, "__PLAYER_INDEX__", playerIndex);
        script = script.Replace("__FIREWORK_BLOCK__", FireworksBlockId.ToString(CultureInfo.InvariantCulture));
        script = script.Replace("__COUNT__", count.ToString(CultureInfo.InvariantCulture));
        return (string?)await context.PushCommandAsync("FUNC " + script) ?? "no-response";
    }

    public static async Task<int> GetWorldModeAsync(GameContext context)
    {
        object? result = await context.PushCommandAsync(
            "FUNC return (int)Game.GameManager.Project.FindSubsystem<Game.SubsystemGameInfo>().WorldSettings.GameMode;");
        return result is int mode ? mode : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<string> DecreaseFireworkCountAsync(GameContext context, int playerIndex, int count)
    {
        string script = GameTools.ReadScript("Elytron/DecreaseFireworkCountCode.cs");
        script = ReplaceInvariant(script, "__PLAYER_INDEX__", playerIndex);
        script = script.Replace("__COUNT__", count.ToString(CultureInfo.InvariantCulture));
        return (string?)await context.PushCommandAsync("FUNC " + script) ?? "no-response";
    }

    public static async Task<bool> IsProjectReadyAsync(GameContext context)
    {
        object? result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
        return result is bool ready && ready;
    }

    public static async Task<int> ResolvePlayerIndexAsync(GameContext client)
    {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not resolved in time.");
        return playerIndex;
    }

    /// <summary>
    /// Creates the world (Survival 模式，滑翔只在该模式下允许) 并把客户端连接上来，最多执行一次。
    /// </summary>
    public static async Task EnsureWorldAndClientAsync(GameContext server, GameContext client)
    {
        if (!(await IsProjectReadyAsync(server)))
        {
            await GameTools.CreateTestWorld(server, "elytron-sync");
        }
        if (!(await IsProjectReadyAsync(client)))
        {
            await GameTools.ConnectTestServer(client, server.GamePort);
        }
    }

    public static bool VelocitiesNear(float ax, float ay, float az, float bx, float by, float bz, float eps)
    {
        float dx = ax - bx;
        float dy = ay - by;
        float dz = az - bz;
        return (dx * dx + dy * dy + dz * dz) <= eps * eps;
    }

    public static Dictionary<string, string> ParseKeyValuePairs(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length == 2)
            {
                result[kv[0]] = kv[1];
            }
        }

        return result;
    }

    private static string ReplaceInvariant(string template, string key, int value)
    {
        return template.Replace(key, value.ToString(CultureInfo.InvariantCulture));
    }
}