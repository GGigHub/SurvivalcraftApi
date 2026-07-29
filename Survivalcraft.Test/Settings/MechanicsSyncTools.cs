using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class MechanicsSyncTools {
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
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync("FUNC " + GameTools.ReadScript("InventorySync/WaitForMainPlayerCode.cs"));
            if (result is bool ready && ready) {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
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
            "FUNC " + GameTools.ReadScript("DamageSync/EnsureServerPlayerEntityCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static Task<int> CreateBoatNearPlayerAsync(GameContext context, int playerIndex, float offsetX, float offsetY, float offsetZ) =>
        CreatureSyncTools.CreateBoatNearPlayerAsync(context, playerIndex, offsetX, offsetY, offsetZ);

    public static async Task<bool> PrepareBoatForMountAsync(
        GameContext context,
        int playerIndex,
        int mountEntityId) =>
        (bool?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/PrepareBoatForMountCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__MOUNT_ENTITY_ID__", mountEntityId.ToString(CultureInfo.InvariantCulture))) ?? false;

    public static Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) =>
        CreatureSyncTools.WaitForEntityExistsAsync(context, entityId);

    public static async Task<SleepState> PrepareSleepAsync(GameContext context, int playerIndex) =>
        ParseSleepState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/PrepareSleepCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<SleepState> StartSleepAsync(GameContext context, int playerIndex) =>
        ParseSleepState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/StartSleepCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<SleepState> WakeUpAsync(GameContext context, int playerIndex) =>
        ParseSleepState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/WakeUpCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<SleepState> GetSleepStateAsync(GameContext context, int playerIndex) =>
        ParseSleepState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetSleepStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<SleepState> WaitForSleepStateAsync(GameContext context, int playerIndex, bool sleeping) {
        SleepState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetSleepStateAsync(context, playerIndex);
            if (state.Exists && state.IsSleeping == sleeping) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Sleep state did not reach sleeping={sleeping} for player {playerIndex}. Actual={state.Raw}.");
    }

    public static async Task<(SleepState Server, SleepState Client)> WakeUpAndWaitForAwakeAsync(
        GameContext client,
        GameContext server,
        int playerIndex) {
        SleepState serverState = default;
        SleepState clientState = default;
        for (int i = 0; i < 50; i++) {
            if (i % 5 == 0) {
                SleepState requested = await WakeUpAsync(client, playerIndex);
                Assert.True(requested.Exists, $"Client did not send wake request. State={requested.Raw}");
            }

            serverState = await GetSleepStateAsync(server, playerIndex);
            clientState = await GetSleepStateAsync(client, playerIndex);
            if (serverState.Exists
                && clientState.Exists
                && !serverState.IsSleeping
                && !clientState.IsSleeping) {
                return (serverState, clientState);
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Sleep state did not reach awake for player {playerIndex}. Server={serverState.Raw}, Client={clientState.Raw}.");
    }

    public static async Task<MountState> StartMountingAsync(GameContext context, int playerIndex, int mountEntityId) =>
        ParseMountState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/StartMountingCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__MOUNT_ENTITY_ID__", mountEntityId.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<MountProtocolState> GetMountProtocolStateAsync(
        GameContext context,
        int playerIndex,
        int mountEntityId) =>
        ParseMountProtocolState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetMountProtocolStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__MOUNT_ENTITY_ID__", mountEntityId.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static Task<MountProtocolState> WaitForMountProtocolReadyAsync(
        GameContext context,
        int playerIndex,
        int mountEntityId) =>
        TestWaiter.UntilAsync(
            () => GetMountProtocolStateAsync(context, playerIndex, mountEntityId),
            state => state.IsReady,
            TestTimeouts.DefaultEventually,
            $"Mount protocol baselines were not ready for player {playerIndex} and mount {mountEntityId}.");

    public static async Task<MountState> StartDismountingAsync(GameContext context, int playerIndex) =>
        ParseMountState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/StartDismountingCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<MountState> GetMountStateAsync(GameContext context, int playerIndex) =>
        ParseMountState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetMountStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<MountState> WaitForMountStateAsync(GameContext context, int playerIndex, int expectedMountId) {
        MountState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetMountStateAsync(context, playerIndex);
            if (state.Exists && state.MountId == expectedMountId && !state.IsAnimating) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Mount state did not reach mount={expectedMountId} for player {playerIndex}. Actual={state.Raw}.");
    }

    public static async Task<EntityFireState> SetPlayerOnFireAsync(GameContext context, int playerIndex, float duration) =>
        ParseEntityFireState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/SetPlayerOnFireCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__DURATION__", duration.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<EntityFireState> GetEntityFireStateAsync(GameContext context, int playerIndex) =>
        ParseEntityFireState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetEntityFireStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<EntityFireState> WaitForEntityFireStateAsync(GameContext context, int playerIndex, bool expected) {
        EntityFireState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetEntityFireStateAsync(context, playerIndex);
            if (state.Exists && state.IsOnFire == expected) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Entity fire state did not reach onFire={expected} for player {playerIndex}. Actual={state.Raw}.");
    }

    public static async Task<FireBlockState> AddFireBlockAsync(GameContext context, int playerIndex) =>
        ParseFireBlockState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/AddFireBlockCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<FireBlockState> RemoveFireBlockAsync(GameContext context, FireBlockState target) =>
        ParseFireBlockState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/RemoveFireBlockCode.cs")
                .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<FireBlockState> GetFireBlockStateAsync(GameContext context, FireBlockState target) =>
        ParseFireBlockState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetFireBlockStateCode.cs")
                .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<FireBlockState> WaitForFireBlockStateAsync(GameContext context, FireBlockState target, bool expected) {
        FireBlockState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetFireBlockStateAsync(context, target);
            if (state.OnFire == expected) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Fire block did not reach onFire={expected}. Actual={state.Raw}.");
    }

    public static async Task<SicknessState> StartSicknessAsync(GameContext context, int playerIndex) =>
        ParseSicknessState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/StartSicknessCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<SicknessState> TriggerNauseaAsync(GameContext context, int playerIndex) =>
        ParseSicknessState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/TriggerNauseaCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<SicknessState> GetSicknessStateAsync(GameContext context, int playerIndex) =>
        ParseSicknessState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetSicknessStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<SicknessState> WaitForSicknessAsync(GameContext context, int playerIndex, bool expected) {
        SicknessState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetSicknessStateAsync(context, playerIndex);
            if (state.Exists && state.IsSick == expected) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Sickness state did not reach sick={expected} for player {playerIndex}. Actual={state.Raw}.");
    }

    public static async Task<SicknessState> WaitForNauseaAsync(GameContext context, int playerIndex) {
        SicknessState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetSicknessStateAsync(context, playerIndex);
            if (state.Exists && state.HasNausea) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Nausea state did not arrive for player {playerIndex}. Actual={state.Raw}.");
    }

    public static async Task<ProjectileState> AddProjectileAsync(GameContext context, int playerIndex) =>
        ParseProjectileState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/AddProjectileCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<ProjectileState> GetProjectileStateAsync(GameContext context, int value) =>
        ParseProjectileState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetProjectileStateCode.cs")
                .Replace("__VALUE__", value.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<ProjectileState> WaitForProjectileAsync(GameContext context, int value) {
        ProjectileState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetProjectileStateAsync(context, value);
            if (state.Count > 0) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Projectile value {value} did not synchronize. Actual={state.Raw}.");
    }

    public static async Task<CrossbowState> GiveCrossbowAsync(GameContext context, int playerIndex) =>
        ParseCrossbowState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GiveCrossbowCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<CrossbowState> PullCrossbowAsync(GameContext context, int playerIndex) =>
        ParseCrossbowState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/PullCrossbowCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<CrossbowState> GetCrossbowStateAsync(GameContext context, int playerIndex) =>
        ParseCrossbowState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetCrossbowStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<CrossbowState> WaitForCrossbowDrawAsync(GameContext context, int playerIndex, int expectedDraw) {
        CrossbowState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetCrossbowStateAsync(context, playerIndex);
            if (state.Exists && state.Draw == expectedDraw) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Crossbow draw did not reach {expectedDraw} for player {playerIndex}. Actual={state.Raw}.");
    }

    public static async Task<ExplosionState> TriggerExplosionAsync(GameContext context, int playerIndex) =>
        ParseExplosionState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/TriggerExplosionCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<ExplosionState> GetExplosionStateAsync(GameContext context, ExplosionState target) =>
        ParseExplosionState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetExplosionStateCode.cs")
                .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<ExplosionState> WaitForExplosionAsync(GameContext context, ExplosionState target) {
        ExplosionState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetExplosionStateAsync(context, target);
            if (state.Cells > 0) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Explosion cells did not synchronize. Actual={state.Raw}.");
    }

    public static async Task<HitValueState> AttackForHitValueAsync(GameContext context, int entityId) =>
        ParseHitValueState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/AttackForHitValueCode.cs")
                .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))) ?? "missing");

    public static async Task<HitValueState> GetHitValueStateAsync(GameContext context, string text) =>
        ParseHitValueState((string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("MechanicsSync/GetHitValueStateCode.cs")
                .Replace("__TEXT__", EscapeScriptString(text))) ?? "missing");

    public static async Task<HitValueState> WaitForHitValueAsync(GameContext context, string text) {
        HitValueState state = default;
        for (int i = 0; i < 50; i++) {
            state = await GetHitValueStateAsync(context, text);
            if (state.Exists) {
                return state;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Hit-value particle {text} did not synchronize. Actual={state.Raw}.");
    }

    private static string EscapeScriptString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static Dictionary<string, string> ParseMap(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

    private static int Int(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;

    private static float Float(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0f;

    private static long Long(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : -1;

    private static bool Bool(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && value == "1";

    public static SleepState ParseSleepState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) return new(false, false, false, raw);
        Dictionary<string, string> map = ParseMap(raw);
        return new(true, Bool(map, "sleeping"), Bool(map, "manual"), raw);
    }

    public static MountState ParseMountState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) return new(false, 0, false, false, raw);
        Dictionary<string, string> map = ParseMap(raw);
        return new(true, Int(map, "mount"), Bool(map, "animating"), Bool(map, "dismounting"), raw);
    }

    public static MountProtocolState ParseMountProtocolState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) return new(-1, -1, -1, raw);
        Dictionary<string, string> map = ParseMap(raw);
        return new(
            Long(map, "riderLifecycle"),
            Long(map, "mountLifecycle"),
            Long(map, "mountRevision"),
            raw);
    }

    public static EntityFireState ParseEntityFireState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) return new(false, false, 0f, raw);
        Dictionary<string, string> map = ParseMap(raw);
        return new(true, Bool(map, "onFire"), Float(map, "duration"), raw);
    }

    public static FireBlockState ParseFireBlockState(string raw) {
        Dictionary<string, string> map = ParseMap(raw);
        return new(Int(map, "x"), Int(map, "y"), Int(map, "z"), Bool(map, "onFire"), raw);
    }

    public static SicknessState ParseSicknessState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) return new(false, false, false, 0f, raw);
        Dictionary<string, string> map = ParseMap(raw);
        return new(true, Bool(map, "sick"), Bool(map, "nausea"), Float(map, "duration"), raw);
    }

    public static ProjectileState ParseProjectileState(string raw) {
        Dictionary<string, string> map = ParseMap(raw);
        return new(Int(map, "count"), Int(map, "value"), raw);
    }

    public static CrossbowState ParseCrossbowState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) return new(false, 0, 0, raw);
        Dictionary<string, string> map = ParseMap(raw);
        return new(true, Int(map, "value"), Int(map, "draw"), raw);
    }

    public static ExplosionState ParseExplosionState(string raw) {
        Dictionary<string, string> map = ParseMap(raw);
        return new(Int(map, "x"), Int(map, "y"), Int(map, "z"), Int(map, "cells"), raw);
    }

    public static HitValueState ParseHitValueState(string raw) {
        if (raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) return new(false, string.Empty, raw);
        Dictionary<string, string> map = ParseMap(raw);
        map.TryGetValue("text", out string? text);
        return new(Bool(map, "exists"), text ?? string.Empty, raw);
    }

    public readonly record struct SleepState(bool Exists, bool IsSleeping, bool AllowManualWakeup, string Raw);
    public readonly record struct MountState(bool Exists, int MountId, bool IsAnimating, bool IsDismounting, string Raw);
    public readonly record struct MountProtocolState(
        long RiderLifecycleRevision,
        long MountLifecycleRevision,
        long MountRevision,
        string Raw) {
        public bool IsReady => RiderLifecycleRevision >= 0
            && MountLifecycleRevision >= 0
            && MountRevision >= 0;
    }
    public readonly record struct EntityFireState(bool Exists, bool IsOnFire, float Duration, string Raw);
    public readonly record struct FireBlockState(int X, int Y, int Z, bool OnFire, string Raw);
    public readonly record struct SicknessState(bool Exists, bool IsSick, bool HasNausea, float Duration, string Raw);
    public readonly record struct ProjectileState(int Count, int Value, string Raw);
    public readonly record struct CrossbowState(bool Exists, int Value, int Draw, string Raw);
    public readonly record struct ExplosionState(int X, int Y, int Z, int Cells, string Raw);
    public readonly record struct HitValueState(bool Exists, string Text, string Raw);
}
