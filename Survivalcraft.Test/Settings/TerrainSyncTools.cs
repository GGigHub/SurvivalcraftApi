using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class TerrainSyncTools {
    public static Task ConnectTestServerAsync(GameContext context, int serverPort) =>
        GameTools.ConnectTestServer(context, serverPort);

    public static async Task<TerrainChunkTarget> PaintChunkPatternAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("TerrainSync/PaintChunkPatternCode.cs")) ?? string.Empty;
        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new TerrainChunkTarget(
            ParseInt(values, "chunkX"),
            ParseInt(values, "chunkZ"),
            ParseInt(values, "sampleY"),
            raw);
    }

    public static async Task<TerrainChunkFingerprint> GetChunkFingerprintAsync(GameContext context, TerrainChunkTarget target) {
        string script = GameTools.ReadScript("TerrainSync/GetChunkFingerprintCode.cs")
            .Replace("__CHUNK_X__", target.ChunkX.ToString(CultureInfo.InvariantCulture))
            .Replace("__CHUNK_Z__", target.ChunkZ.ToString(CultureInfo.InvariantCulture))
            .Replace("__SAMPLE_Y__", target.SampleY.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseFingerprint(raw);
    }

    public static Task RequestChunkSyncAsync(GameContext context, TerrainChunkTarget target) {
        string script = GameTools.ReadScript("TerrainSync/RequestChunkSyncCode.cs")
            .Replace("__CHUNK_X__", target.ChunkX.ToString(CultureInfo.InvariantCulture))
            .Replace("__CHUNK_Z__", target.ChunkZ.ToString(CultureInfo.InvariantCulture));

        return context.PushCommandAsync("FUNC " + script);
    }

    public static async Task<TerrainCellState> ChangePatternCellAsync(GameContext context, TerrainChunkTarget target) {
        string script = GameTools.ReadScript("TerrainSync/ChangeChunkPatternCellCode.cs")
            .Replace("__CHUNK_X__", target.ChunkX.ToString(CultureInfo.InvariantCulture))
            .Replace("__CHUNK_Z__", target.ChunkZ.ToString(CultureInfo.InvariantCulture))
            .Replace("__SAMPLE_Y__", target.SampleY.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new TerrainCellState(
            ParseInt(values, "Value"),
            ParseInt(values, "Contents"),
            ParseInt(values, "Data"),
            raw);
    }

    public static async Task<TerrainChunkFingerprint> WaitForChunkFingerprintAsync(
        GameContext context,
        TerrainChunkTarget target,
        TerrainChunkFingerprint expected) {
        TerrainChunkFingerprint state = default;
        for (int i = 0; i < 120; i++) {
            if (i % 10 == 0) {
                await RequestChunkSyncAsync(context, target);
            }

            state = await GetChunkFingerprintAsync(context, target);
            if (FingerprintsMatch(expected, state)) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<TerrainChunkFingerprint> WaitForChunkCellSamplesAsync(
        GameContext context,
        TerrainChunkTarget target,
        TerrainChunkFingerprint expected) {
        TerrainChunkFingerprint state = default;
        for (int i = 0; i < 120; i++) {
            if (i % 10 == 0) {
                await RequestChunkSyncAsync(context, target);
            }

            state = await GetChunkFingerprintAsync(context, target);
            if (CellSamplesMatch(expected, state)) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<(TerrainChunkFingerprint Expected, TerrainChunkFingerprint Actual)> WaitForMatchingChunkFingerprintAsync(
        GameContext expectedContext,
        GameContext actualContext,
        TerrainChunkTarget target) {
        TerrainChunkFingerprint expected = default;
        TerrainChunkFingerprint actual = default;
        for (int i = 0; i < 180; i++) {
            if (i % 10 == 0) {
                await RequestChunkSyncAsync(actualContext, target);
            }

            expected = await GetChunkFingerprintAsync(expectedContext, target);
            actual = await GetChunkFingerprintAsync(actualContext, target);
            if (FingerprintsMatch(expected, actual)) {
                return (expected, actual);
            }

            await Task.Delay(100);
        }

        return (expected, actual);
    }

    public static async Task<TerrainChunkFingerprint> WaitForLoadedChunkAsync(GameContext context, TerrainChunkTarget target) {
        TerrainChunkFingerprint state = default;
        for (int i = 0; i < 120; i++) {
            state = await GetChunkFingerprintAsync(context, target);
            if (state.Loaded) {
                return state;
            }

            await Task.Delay(100);
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

    public static async Task ConnectSpectatorAsync(GameContext spectator, int serverPort) {
        if (!spectator.IsStarted) {
            spectator.Start();
            bool mainMenu = await spectator.WaitLogAsync("Entered screen \"MainMenu\"", 60);
            Assert.True(mainMenu, $"Spectator did not enter MainMenu. Recent logs:{Environment.NewLine}{spectator.GetRecentLogText()}");
        }

        await ConnectTestServerAsync(spectator, serverPort);
        Assert.True(await WaitForMainPlayerAsync(spectator), "Spectator main player was not ready.");
    }

    public static bool FingerprintsMatch(TerrainChunkFingerprint expected, TerrainChunkFingerprint actual) =>
        expected.Loaded
        && actual.Loaded
        && expected.ChunkX == actual.ChunkX
        && expected.ChunkZ == actual.ChunkZ
        && expected.SampleY == actual.SampleY
        && expected.CellsHash == actual.CellsHash
        && expected.ShaftsHash == actual.ShaftsHash
        && expected.V1 == actual.V1
        && expected.V2 == actual.V2
        && expected.V3 == actual.V3
        && expected.V4 == actual.V4
        && expected.C1 == actual.C1
        && expected.C2 == actual.C2
        && expected.C3 == actual.C3
        && expected.C4 == actual.C4
        && expected.D1 == actual.D1
        && expected.D2 == actual.D2
        && expected.D3 == actual.D3
        && expected.D4 == actual.D4;

    public static bool CellSamplesMatch(TerrainChunkFingerprint expected, TerrainChunkFingerprint actual) =>
        expected.Loaded
        && actual.Loaded
        && expected.ChunkX == actual.ChunkX
        && expected.ChunkZ == actual.ChunkZ
        && expected.SampleY == actual.SampleY
        && expected.V1 == actual.V1
        && expected.V2 == actual.V2
        && expected.V3 == actual.V3
        && expected.V4 == actual.V4
        && expected.C1 == actual.C1
        && expected.C2 == actual.C2
        && expected.C3 == actual.C3
        && expected.C4 == actual.C4
        && expected.D1 == actual.D1
        && expected.D2 == actual.D2
        && expected.D3 == actual.D3
        && expected.D4 == actual.D4;

    public static TerrainChunkFingerprint ParseFingerprint(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        bool loaded = ParseBool(values, "loaded");
        return new TerrainChunkFingerprint(
            ParseInt(values, "chunkX"),
            ParseInt(values, "chunkZ"),
            loaded,
            values.TryGetValue("threadState", out string? threadState) ? threadState : string.Empty,
            values.TryGetValue("state", out string? state) ? state : string.Empty,
            ParseInt(values, "sampleY"),
            ParseInt(values, "cellsHash"),
            ParseInt(values, "shaftsHash"),
            ParseInt(values, "v1"),
            ParseInt(values, "v2"),
            ParseInt(values, "v3"),
            ParseInt(values, "v4"),
            ParseInt(values, "c1"),
            ParseInt(values, "c2"),
            ParseInt(values, "c3"),
            ParseInt(values, "c4"),
            ParseInt(values, "d1"),
            ParseInt(values, "d2"),
            ParseInt(values, "d3"),
            ParseInt(values, "d4"),
            value);
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

    private static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && bool.TryParse(value, out bool result)
        && result;

    public readonly record struct TerrainChunkTarget(int ChunkX, int ChunkZ, int SampleY, string Raw);

    public readonly record struct TerrainCellState(int Value, int Contents, int Data, string Raw);

    public readonly record struct TerrainChunkFingerprint(
        int ChunkX,
        int ChunkZ,
        bool Loaded,
        string ThreadState,
        string State,
        int SampleY,
        int CellsHash,
        int ShaftsHash,
        int V1,
        int V2,
        int V3,
        int V4,
        int C1,
        int C2,
        int C3,
        int C4,
        int D1,
        int D2,
        int D3,
        int D4,
        string Raw);
}
