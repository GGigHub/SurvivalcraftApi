using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

internal static class SubnauticaSyncTools {
    public static async Task<bool> IsProjectReadyAsync(GameContext context) {
        object result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
        return result is bool ready && ready;
    }

    public static async Task<string> GetModLoadStateAsync(GameContext context) {
        return ((string?)await context.PushCommandAsync("FUNC " + Read("GetModLoadStateCode.cs"))) ?? string.Empty;
    }

    public static async Task<Point3> GetPlayerCellAsync(GameContext context) {
        string[] values = (((string?)await context.PushCommandAsync(
            "FUNC var p = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().GlobalSpawnPosition + new Engine.Vector3(6f, 2f, 6f); var c = Game.Terrain.ToCell(p); return string.Join(\",\", c.X, c.Y, c.Z);"))
            ?? string.Empty).Split(',');
        return new Point3(
            int.Parse(values[0], CultureInfo.InvariantCulture),
            int.Parse(values[1], CultureInfo.InvariantCulture),
            int.Parse(values[2], CultureInfo.InvariantCulture));
    }

    public static async Task<string> PlaceSoulTorchAsync(GameContext context, Point3 origin) {
        string script = Read("PlaceSoulTorchCode.cs")
            .Replace("__X__", origin.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", origin.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", origin.Z.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> GetSoulTorchRegistryAsync(GameContext context, Point3 torchCell, double probeX, double probeY, double probeZ) {
        string script = Read("GetSoulTorchRegistryCode.cs")
            .Replace("__PX__", (torchCell.X + 0.5).ToString("R", CultureInfo.InvariantCulture) + "d")
            .Replace("__PY__", (torchCell.Y + 0.5).ToString("R", CultureInfo.InvariantCulture) + "d")
            .Replace("__PZ__", (torchCell.Z + 0.5).ToString("R", CultureInfo.InvariantCulture) + "d")
            .Replace("__FX__", probeX.ToString("R", CultureInfo.InvariantCulture) + "d")
            .Replace("__FY__", probeY.ToString("R", CultureInfo.InvariantCulture) + "d")
            .Replace("__FZ__", probeZ.ToString("R", CultureInfo.InvariantCulture) + "d");
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> PlaceModBlockPaletteAsync(GameContext context, Point3 origin) {
        // 在高空放置，避免附近水域生成的水晶珊瑚方块触发 mod 的
        // BubbleCoralBlockBehavior.OnNeighborBlockChanged 越界崩溃。
        int y = origin.Y + 30;
        string script = Read("PlaceModBlockPaletteCode.cs")
            .Replace("__X__", (origin.X + 10).ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", origin.Z.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<int> SpawnCreatureAsync(GameContext context, string templateName, Vector3 position) {
        string script = Read("SpawnSubnauticaCreatureCode.cs")
            .Replace("__TEMPLATE__", templateName)
            .Replace("__X__", position.X.ToString("R", CultureInfo.InvariantCulture) + "f")
            .Replace("__Y__", position.Y.ToString("R", CultureInfo.InvariantCulture) + "f")
            .Replace("__Z__", position.Z.ToString("R", CultureInfo.InvariantCulture) + "f");
        object result = await context.PushCommandAsync("FUNC " + script);
        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<bool> EntityExistsAsync(GameContext context, int entityId) {
        string script = Read("EntityExistsCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));
        object result = await context.PushCommandAsync("FUNC " + script);
        return result is bool exists && exists;
    }

    public static async Task WaitForEntityAsync(GameContext context, int entityId) {
        await TestWaiter.UntilAsync(
            () => EntityExistsAsync(context, entityId),
            exists => exists,
            TestTimeouts.DefaultEventually,
            context.RoleName + " did not receive entity " + entityId + ".");
    }

    public static async Task<int> GetCellValueAsync(GameContext context, Point3 cell) {
        string script = Read("GetCellValueCode.cs")
            .Replace("__X__", cell.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", cell.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", cell.Z.ToString(CultureInfo.InvariantCulture));
        object result = await context.PushCommandAsync("FUNC " + script);
        return result is int value ? value : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<int> WaitForCellValueAsync(GameContext context, Point3 cell, int expected, int maxTries = 150) {
        int value = -1;
        for (int i = 0; i < maxTries; i++) {
            value = await GetCellValueAsync(context, cell);
            if (value == expected) {
                return value;
            }

            if (i % 10 == 9) {
                await RequestChunkSyncForCellAsync(context, cell);
            }

            await Task.Delay(100);
        }

        return value;
    }

    public static async Task<string> GetTerrainFingerprintAsync(GameContext context, Point3 origin, int minX, int minY, int minZ, int maxX, int maxY, int maxZ) {
        string script = string.Create(
            CultureInfo.InvariantCulture,
            $"FUNC var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>().Terrain;\nint hash = unchecked((int)2166136261u);\nfor (int x = {origin.X + minX}; x <= {origin.X + maxX}; x++) {{\n    for (int y = {origin.Y + minY}; y <= {origin.Y + maxY}; y++) {{\n        for (int z = {origin.Z + minZ}; z <= {origin.Z + maxZ}; z++) {{\n            hash = unchecked((hash ^ Game.Terrain.ReplaceLight(terrain.GetCellValue(x, y, z), 0)) * 16777619);\n        }}\n    }}\n}}\nreturn hash.ToString(System.Globalization.CultureInfo.InvariantCulture);");
        return ((string?)await context.PushCommandAsync(script)) ?? string.Empty;
    }

    public static async Task<string> GetEelShockStateAsync(GameContext context, int entityId) {
        string script = Read("GetEelShockStateCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> ForceEelShockStateAsync(GameContext context, int entityId) {
        string script = Read("ForceEelShockStateCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> FeedMermaidAsync(GameContext context, int entityId) {
        string script = Read("FeedMermaidCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> GetMermaidStateAsync(GameContext context, int entityId) {
        string script = Read("GetMermaidStateCode.cs")
            .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<int> GetPlayerEntityIdAsync(GameContext context, int playerIndex) {
        string script = Read("GetPlayerEntityIdCode.cs")
            .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
        object result = await context.PushCommandAsync("FUNC " + script);
        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<string> UnlockAchievementAsync(GameContext context, int playerEntityId) {
        string script = Read("UnlockAchievementCode.cs")
            .Replace("__PLAYER_ENTITY_ID__", playerEntityId.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> GetAchievementsStateAsync(GameContext context, int playerEntityId) {
        string script = Read("GetAchievementsStateCode.cs")
            .Replace("__PLAYER_ENTITY_ID__", playerEntityId.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> TriggerKrakenEncounterAsync(GameContext context, Point3 triggerCell, int playerIndex) {
        string script = Read("TriggerKrakenEncounterCode.cs")
            .Replace("__TX__", triggerCell.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__TY__", triggerCell.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__TZ__", triggerCell.Z.ToString(CultureInfo.InvariantCulture))
            .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
        return ((string?)await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
    }

    public static async Task<string> GetKrakenEncounterStateAsync(GameContext context) {
        return ((string?)await context.PushCommandAsync("FUNC " + Read("GetKrakenEncounterStateCode.cs"))) ?? string.Empty;
    }

    public static Dictionary<string, string> ParseFingerprint(string value) {
        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    public static void AssertStateEquals(string featureName, string serverState, string clientState) {
        Assert.True(
            string.Equals(serverState, clientState, StringComparison.Ordinal),
            $"{featureName} 两端不一致：{Environment.NewLine}服务端={serverState}{Environment.NewLine}客户端={clientState}");
    }

    public static void AssertStateDiffers(string featureName, string serverState, string clientState) {
        Assert.True(
            !string.Equals(serverState, clientState, StringComparison.Ordinal),
            $"{featureName} 两端意外一致（预期不同步但实际同步了）：{Environment.NewLine}服务端={serverState}{Environment.NewLine}客户端={clientState}");
    }

    private static async Task RequestChunkSyncForCellAsync(GameContext context, Point3 cell) {
        int chunkX = cell.X >> Game.TerrainChunk.SizeBits;
        int chunkZ = cell.Z >> Game.TerrainChunk.SizeBits;
        string script = GameTools.ReadScript("TerrainSync/RequestChunkSyncCode.cs")
            .Replace("__CHUNK_X__", chunkX.ToString(CultureInfo.InvariantCulture))
            .Replace("__CHUNK_Z__", chunkZ.ToString(CultureInfo.InvariantCulture));
        await context.PushCommandAsync("FUNC " + script);
    }

    private static string Read(string fileName) => GameTools.ReadScript("Subnautica/" + fileName);
}