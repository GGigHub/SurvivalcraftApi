using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

public static class TerrainStreamingTools {
    public const float VisibilityDistance = 64f;
    private const int TerrainLoadAttempts = 180;

    public static Task SetVisibilityDistanceAsync(GameContext context, float visibilityDistance = VisibilityDistance) =>
        context.PushCommandAsync($"EXEC SettingsManager.VisibilityRange = {visibilityDistance.ToString(CultureInfo.InvariantCulture)}; SettingsManager.SaveSettings();");

    public static async Task<ChunkLoadSnapshot> GetChunkLoadSnapshotAsync(GameContext context, int playerIndex = -1, float visibilityDistance = VisibilityDistance) {
        string script = GameTools.ReadScript("TerrainStreaming/GetChunkLoadSnapshotCode.cs")
            .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
            .Replace("__RADIUS__", visibilityDistance.ToString(CultureInfo.InvariantCulture));
        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        Dictionary<string, string> values = ParseKeyValuePairs(raw);

        return new ChunkLoadSnapshot(
            ParseBool(values, "ready"),
            ParseInt(values, "playerIndex", -1),
            new Vector3Dto(ParseFloat(values, "bodyX"), ParseFloat(values, "bodyY"), ParseFloat(values, "bodyZ")),
            ParseBool(values, "hasLocation"),
            new Vector2Dto(ParseFloat(values, "locationX"), ParseFloat(values, "locationZ")),
            ParseFloat(values, "visibility"),
            ParseFloat(values, "content"),
            ParseInt(values, "totalAllocated", 0),
            ParseInt(values, "totalLoaded", 0),
            ParseInt(values, "allocated", 0),
            ParseInt(values, "loaded", 0),
            ParseCoordinates(values.GetValueOrDefault("loadedCoordinates", string.Empty)),
            raw);
    }

    public static async Task<ChunkLoadState> GetChunkLoadStateAsync(GameContext context, int chunkX, int chunkZ) {
        string script = GameTools.ReadScript("TerrainStreaming/GetChunkLoadStateCode.cs")
            .Replace("__CHUNK_X__", chunkX.ToString(CultureInfo.InvariantCulture))
            .Replace("__CHUNK_Z__", chunkZ.ToString(CultureInfo.InvariantCulture));
        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new ChunkLoadState(
            ParseBool(values, "allocated"),
            ParseBool(values, "loaded"),
            values.GetValueOrDefault("threadState", string.Empty),
            values.GetValueOrDefault("state", string.Empty),
            raw);
    }

    public static async Task<ChunkLoadSnapshot> WaitForFullyLoadedRadiusAsync(GameContext context, int playerIndex = -1, float visibilityDistance = VisibilityDistance, int attempts = TerrainLoadAttempts) {
        ChunkLoadSnapshot snapshot = default;
        for (int i = 0; i < attempts; i++) {
            snapshot = await GetChunkLoadSnapshotAsync(context, playerIndex, visibilityDistance);
            if (snapshot.Ready && snapshot.AllocatedChunkCount > 0 && snapshot.LoadedChunkCount == snapshot.AllocatedChunkCount) {
                return snapshot;
            }

            await Task.Delay(100);
        }

        return snapshot;
    }

    public static async Task<ChunkLoadState> WaitForLoadedChunkAsync(GameContext context, int chunkX, int chunkZ) {
        ChunkLoadState state = default;
        for (int i = 0; i < TerrainLoadAttempts; i++) {
            state = await GetChunkLoadStateAsync(context, chunkX, chunkZ);
            if (state.Loaded) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<ChunkLoadSnapshot> WaitForExpectedLocationAsync(GameContext context, int playerIndex = -1, float visibilityDistance = VisibilityDistance) {
        ChunkLoadSnapshot snapshot = default;
        for (int i = 0; i < 120; i++) {
            snapshot = await GetChunkLoadSnapshotAsync(context, playerIndex, visibilityDistance);
            if (IsAtExpectedLocation(snapshot, visibilityDistance)) {
                return snapshot;
            }

            await Task.Delay(100);
        }

        return snapshot;
    }

    public static bool IsAtExpectedLocation(ChunkLoadSnapshot snapshot, float visibilityDistance = VisibilityDistance) =>
        snapshot.HasUpdateLocation
        && Math.Abs(snapshot.VisibilityDistance - visibilityDistance) < 0.01f
        && snapshot.ContentDistance >= visibilityDistance - 0.01f
        && Vector2Dto.DistanceSquared(
            new Vector2Dto(snapshot.BodyPosition.X, snapshot.BodyPosition.Z),
            snapshot.UpdateCenter) < 1f;

    public static (int X, int Z) GetForwardEdgeChunk(ChunkLoadSnapshot snapshot, float visibilityDistance = VisibilityDistance) {
        int chunkX = (int)MathF.Floor((snapshot.BodyPosition.X + visibilityDistance - 16f) / 16f);
        int chunkZ = (int)MathF.Floor(snapshot.BodyPosition.Z / 16f);
        return (chunkX, chunkZ);
    }

    private static HashSet<string> ParseCoordinates(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, string> ParseKeyValuePairs(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);

    private static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && bool.TryParse(value, out bool result)
        && result;

    private static int ParseInt(Dictionary<string, string> values, string key, int defaultValue) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : defaultValue;

    private static float ParseFloat(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;
}

public readonly record struct ChunkLoadSnapshot(
    bool Ready,
    int PlayerIndex,
    Vector3Dto BodyPosition,
    bool HasUpdateLocation,
    Vector2Dto UpdateCenter,
    float VisibilityDistance,
    float ContentDistance,
    int TotalAllocatedChunkCount,
    int TotalLoadedChunkCount,
    int AllocatedChunkCount,
    int LoadedChunkCount,
    HashSet<string> LoadedChunkCoordinates,
    string Raw);

public readonly record struct ChunkLoadState(
    bool Allocated,
    bool Loaded,
    string ThreadState,
    string State,
    string Raw);
