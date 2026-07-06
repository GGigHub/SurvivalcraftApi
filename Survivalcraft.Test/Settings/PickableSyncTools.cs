using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class PickableSyncTools {
    public const float SyncedPositionEpsilonSquared = 0.25f;
    public const int TestBlockContents = 2;
    public static async Task<int> CreatePickableAsync(GameContext context, int count, float xOffset, float yOffset, float zOffset) {
        object? result = await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PickableSync/CreatePickableCode.cs").Replace("__X_OFFSET__", xOffset.ToString(CultureInfo.InvariantCulture)).Replace("__Y_OFFSET__", yOffset.ToString(CultureInfo.InvariantCulture)).Replace("__Z_OFFSET__", zOffset.ToString(CultureInfo.InvariantCulture)).Replace("__CONTENTS__", TestBlockContents.ToString(CultureInfo.InvariantCulture)).Replace("__COUNT__", count.ToString(CultureInfo.InvariantCulture)));
        return (int?)result ?? 0;
    }

    public static async Task<int> RemovePickableAsync(GameContext context, int pickableId) {
        object? result = await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PickableSync/RemovePickableCode.cs").Replace("__PICKABLE_ID__", pickableId.ToString(CultureInfo.InvariantCulture)));
        return (int?)result ?? 0;
    }

    public static async Task<PickableState> ForcePickablePositionAsync(GameContext context, int pickableId, float xOffset, float yOffset, float zOffset) {
        string state = (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PickableSync/ForcePickablePositionCode.cs").Replace("__PICKABLE_ID__", pickableId.ToString(CultureInfo.InvariantCulture)).Replace("__X_OFFSET__", xOffset.ToString(CultureInfo.InvariantCulture)).Replace("__Y_OFFSET__", yOffset.ToString(CultureInfo.InvariantCulture)).Replace("__Z_OFFSET__", zOffset.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
        return ParsePickableState(state);
    }

    public static async Task<PickableState> CreateAttractablePickableAsync(GameContext context) {
        string state = (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PickableSync/CreateAttractablePickableCode.cs").Replace("__CONTENTS__", TestBlockContents.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
        return ParsePickableState(state);
    }

    public static async Task<PickableState> AttractPickableAsync(GameContext context, int pickableId) {
        string state = (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PickableSync/AttractPickableCode.cs").Replace("__PICKABLE_ID__", pickableId.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
        return ParsePickableState(state);
    }

    public static Task DelayPickableAutoPickupAsync(GameContext context, int pickableId) =>
        context.PushCommandAsync("EXEC " + GameTools.ReadScript("PickableSync/DelayPickableAutoPickupCode.cs").Replace("__PICKABLE_ID__", pickableId.ToString(CultureInfo.InvariantCulture)));

    public static async Task<bool> WaitForPickableExistsAsync(GameContext context, int pickableId) {
        for (int i = 0; i < 50; i++) {
            if ((await GetPickableStateAsync(context, pickableId)).Exists) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    public static async Task ConnectTestServerForPickableTest(GameContext client, int serverPort) {
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 45);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"", 45);
        Assert.True(playerScreen, "Failed to load the Player screen");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        for (int i = 0; i < 450; i++) {
            bool gameReady = (bool?)await client.PushCommandAsync(
                "FUNC return Game.GameManager.Project != null && ScreensManager.CurrentScreen?.GetType().Name == \"GameScreen\";") ?? false;
            if (gameReady) {
                return;
            }
            await Task.Delay(100);
        }

        Assert.Fail("Client did not enter the game screen after pressing PlayButton.");
    }

    public static async Task<PickableState> WaitForPickableStateAsync(GameContext context, int pickableId) {
        PickableState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetPickableStateAsync(context, pickableId);
            if (state.Exists) {
                return state;
            }
            await Task.Delay(100);
        }
        return state;
    }

    public static async Task<bool> WaitForPickableMissingAsync(GameContext context, int pickableId) {
        for (int i = 0; i < 50; i++) {
            if (!(await GetPickableStateAsync(context, pickableId)).Exists) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    public static async Task<PickableState> GetPickableStateAsync(GameContext context, int pickableId) {
        string state = (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PickableSync/GetPickableStateCode.cs").Replace("__PICKABLE_ID__", pickableId.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
        return ParsePickableState(state);
    }

    public static bool PositionsMatch(PickableState expected, PickableState actual) =>
        expected.Exists
        && actual.Exists
        && PickableVector3Dto.DistanceSquared(expected.Position, actual.Position) <= SyncedPositionEpsilonSquared;

    public static PickableState ParsePickableState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new PickableState(false, 0, 0, 0, default, null, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new PickableState(
            true,
            ParseInt(values, "id"),
            ParseInt(values, "value"),
            ParseInt(values, "count"),
            ParseVector3(values["pos"]),
            values.TryGetValue("fly", out string? flyValue) && !flyValue.Equals("null", StringComparison.OrdinalIgnoreCase)
                ? ParseVector3(flyValue)
                : null,
            value);
    }

    public static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    public static PickableVector3Dto ParseVector3(string value) {
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
            return new PickableVector3Dto(x, y, z);
        }
        throw new InvalidOperationException($"Invalid Vector3 value: '{value}'.");
    }
}

public readonly record struct PickableVector3Dto(float X, float Y, float Z) {
    public static float DistanceSquared(PickableVector3Dto v1, PickableVector3Dto v2) {
        float x = v1.X - v2.X;
        float y = v1.Y - v2.Y;
        float z = v1.Z - v2.Z;
        return x * x + y * y + z * z;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R}, {Z:R})");
}

public readonly record struct PickableState(
    bool Exists,
    int Id,
    int Value,
    int Count,
    PickableVector3Dto Position,
    PickableVector3Dto? FlyToPosition,
    string Raw) {
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(Exists={Exists}, Id={Id}, Value={Value}, Count={Count}, Position={Position}, FlyToPosition={FlyToPosition}, Raw='{Raw}')");
}
