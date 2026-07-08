using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class PlayerSyncTools {
    public const float LookAnglesEpsilonSquared = 0.0004f;
    public const float LevelEpsilon = 0.0001f;
    public const float HealthEpsilon = 0.001f;
    public const int BoatBlockIndex = 178;
    public const int WoodenDoorBlockIndex = 56;
    public const int PlanksBlockIndex = 21;
    public const int CoalChunkBlockIndex = 31;
    public const int SnowballBlockIndex = 105;

    public static async Task ConnectObserverAsync(GameContext observer, int serverPort) {
        if (!observer.IsStarted) {
            observer.Start();
            bool mainMenuEntered = await observer.WaitLogAsync("Entered screen \"MainMenu\"");
            Assert.True(mainMenuEntered, $"Observer failed to enter MainMenu. Logs:{Environment.NewLine}{observer.GetRecentLogText()}");
        }

        await observer.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await observer.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 45);
        Assert.True(connected, $"Observer failed to connect to the server. Logs:{Environment.NewLine}{observer.GetRecentLogText()}");

        bool playerScreen = await observer.WaitLogAsync("Entered screen \"Player\"", 45);
        Assert.True(playerScreen, $"Observer failed to load the Player screen. Logs:{Environment.NewLine}{observer.GetRecentLogText()}");
    }

    public static async Task<int> ConnectAndCreatePlayerAsync(GameContext client, int serverPort, string playerName) {
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString(CultureInfo.InvariantCulture)));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 45);
        Assert.True(connected, $"Client failed to connect to the server. Logs:{Environment.NewLine}{client.GetRecentLogText()}");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"", 45);
        Assert.True(playerScreen, $"Client failed to load the Player screen. Logs:{Environment.NewLine}{client.GetRecentLogText()}");

        string prepared = string.Empty;
        for (int i = 0; i < 60; i++) {
            prepared = (string?)await client.PushCommandAsync("FUNC " + GameTools.ReadScript("PlayerSync/PreparePlayerOnPlayerScreenCode.cs").Replace("__PLAYER_NAME__", playerName)) ?? string.Empty;
            if (!prepared.Contains("not-player-screen", StringComparison.OrdinalIgnoreCase)
                && !prepared.Contains("player-screen-not-ready", StringComparison.OrdinalIgnoreCase)) {
                break;
            }
            await Task.Delay(100);
        }
        Assert.DoesNotContain("not-player-screen", prepared);
        Assert.DoesNotContain("player-screen-not-ready", prepared);

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool intoPlaying = await client.WaitLogAsync("Player into playing.", 60);
        Assert.True(intoPlaying, $"Client failed into playing state. Logs:{Environment.NewLine}{client.GetRecentLogText()}");

        PlayerState player = await WaitForMainPlayerAsync(client);
        Assert.True(player.Index >= 0, $"Client main player has invalid index. Player={player}.");
        return player.Index;
    }

    public static async Task<PlayerState> ModifyMainPlayerAsync(GameContext client, string playerName) {
        string raw = (string?)await client.PushCommandAsync("FUNC " + GameTools.ReadScript("PlayerSync/ModifyMainPlayerCode.cs").Replace("__PLAYER_NAME__", playerName)) ?? string.Empty;
        Assert.DoesNotContain("no-project", raw);
        Assert.DoesNotContain("no-main-player", raw);
        Assert.Contains("event=True", raw, StringComparison.OrdinalIgnoreCase);

        PlayerState player = await WaitForMainPlayerAsync(client);
        Assert.Equal(playerName, player.Name);
        return player;
    }

    public static async Task CreateCreativeTestWorldAsync(GameContext server, string seed) {
        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playScreenEntered = await server.WaitLogAsync("Entered screen \"Play\"");
        Assert.True(playScreenEntered, "Failed to enter the Play screen");

        await server.PushCommandAsync("CLICK_WIDGET NewWorld");
        bool newWorldScreenEntered = await server.WaitLogAsync("Entered screen \"NewWorld\"");
        Assert.True(newWorldScreenEntered, "Failed to enter the New World screen");

        if (!string.IsNullOrEmpty(seed)) {
            for (int i = 0; i < 50; i++) {
                object? isReady = await server.PushCommandAsync("FUNC return (ScreensManager.CurrentScreen as NewWorldScreen)?.m_worldSettings != null;");
                if (isReady is bool ready && ready) {
                    break;
                }

                await Task.Delay(100);
            }

            await server.PushCommandAsync($"EXEC (ScreensManager.CurrentScreen as NewWorldScreen).m_seedTextBox.Text = \"{seed}\";");
        }

        string modeResult = string.Empty;
        for (int i = 0; i < 50; i++) {
            modeResult = (string?)await server.PushCommandAsync("FUNC " + GameTools.ReadScript("PlayerSync/SetNewWorldModeCode.cs")
                .Replace("__GAME_MODE__", "Creative")) ?? string.Empty;
            if (string.Equals(modeResult, "Creative", StringComparison.Ordinal)) {
                break;
            }

            await Task.Delay(100);
        }

        Assert.Equal("Creative", modeResult);

        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playerScreen = await server.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen");

        await server.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await server.WaitLogAsync("Entered screen \"Game\"");
        Assert.True(gameEntered, "Failed to enter the Game screen");
    }

    public static Task ConnectTestServerAsync(GameContext context, int serverPort) =>
        GameTools.ConnectTestServer(context, serverPort);

    public static async Task ConnectSpectatorAsync(GameFixture fixture, int serverPort) {
        fixture.StartSpectator();
        await ConnectTestServerAsync(fixture.Spectator, serverPort);
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

    public static async Task<PlayerRuntimeState> GetPlayerStateAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetPlayerRuntimeStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;

        return ParsePlayerRuntimeState(raw);
    }

    public static async Task<PlayerRuntimeState> WaitForPlayerEntityAsync(GameContext context, int playerIndex) {
        PlayerRuntimeState state = default;
        for (int i = 0; i < 120; i++) {
            state = await GetPlayerStateAsync(context, playerIndex);
            if (state.Exists && state.HasEntity && state.EntityId > 0) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<PlayerRuntimeState> WaitForCreativeFlyAsync(GameContext context, int playerIndex, bool expected) {
        PlayerRuntimeState state = default;
        for (int i = 0; i < 80; i++) {
            state = await GetPlayerStateAsync(context, playerIndex);
            if (state.Exists && state.HasEntity && state.IsCreativeFlyEnabled == expected) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<PlayerRuntimeState> WaitForLevelAsync(GameContext context, int playerIndex, float expectedLevel) {
        PlayerRuntimeState state = default;
        for (int i = 0; i < 100; i++) {
            state = await GetPlayerStateAsync(context, playerIndex);
            if (state.Exists && Math.Abs(state.Level - expectedLevel) <= LevelEpsilon) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<PlayerRuntimeState> WaitForDeadAsync(GameContext context, int playerIndex) {
        PlayerRuntimeState state = default;
        for (int i = 0; i < 120; i++) {
            state = await GetPlayerStateAsync(context, playerIndex);
            if (state.Exists
                && state.Health <= 0f) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<PlayerRuntimeState> WaitForRespawnedAsync(GameContext context, int playerIndex, int oldEntityId) {
        PlayerRuntimeState state = default;
        for (int i = 0; i < 220; i++) {
            state = await GetPlayerStateAsync(context, playerIndex);
            if (state.Exists
                && state.HasEntity
                && state.EntityId > 0
                && state.EntityId != oldEntityId
                && state.Health > 0f
                && string.Equals(state.State, "Playing", StringComparison.Ordinal)) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<PlayerRuntimeState> AddExperienceAsync(GameContext context, int playerIndex, int count) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/AddExperienceCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__COUNT__", count.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;

        Assert.DoesNotContain("missing", raw, StringComparison.OrdinalIgnoreCase);
        return await GetPlayerStateAsync(context, playerIndex);
    }

    public static async Task<PlayerRuntimeState> KillPlayerAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/KillPlayerCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;

        Assert.DoesNotContain("missing", raw, StringComparison.OrdinalIgnoreCase);
        return ParsePlayerRuntimeState(raw);
    }

    public static async Task<string> RequestRespawnAsync(GameContext context) =>
        (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PlayerSync/RequestRespawnCode.cs")) ?? string.Empty;

    public static async Task<List<PlayerState>> GetPlayersAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync("FUNC " + GameTools.ReadScript("PlayerSync/GetPlayersStateCode.cs")) ?? string.Empty;
        return ParsePlayers(raw);
    }

    public static async Task<PlayerBodyStatusState> GetPlayerBodyStatusAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetPlayerBodyStatusCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
        return ParsePlayerBodyStatus(raw);
    }

    public static async Task<PlayerBodyStatusState> SetMainPlayerBodyStatusAsync(
        GameContext context,
        Vector2Dto lookAngles,
        bool isCrouching) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/SetMainPlayerBodyStatusCode.cs")
                .Replace("__LOOK_X__", lookAngles.X.ToString(CultureInfo.InvariantCulture))
                .Replace("__LOOK_Y__", lookAngles.Y.ToString(CultureInfo.InvariantCulture))
                .Replace("__CROUCH_FACTOR__", (isCrouching ? 1f : 0f).ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
        return ParsePlayerBodyStatus(raw);
    }

    public static async Task<PlayerBodyStatusState> WaitForPlayerBodyStatusNearAsync(
        GameContext context,
        int playerIndex,
        PlayerBodyStatusState expected) {
        PlayerBodyStatusState state = default;
        for (int i = 0; i < 60; i++) {
            state = await GetPlayerBodyStatusAsync(context, playerIndex);
            if (PlayerBodyStatusNear(expected, state)) {
                return state;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Player body status did not synchronize. Expected={expected}, Actual={state}.");
    }

    public static async Task<PlayerState> WaitForMainPlayerAsync(GameContext context) {
        PlayerState player = default;
        for (int i = 0; i < 60; i++) {
            player = (await GetPlayersAsync(context)).FirstOrDefault(p => p.IsMain);
            if (player.Exists) {
                return player;
            }
            await Task.Delay(100);
        }
        return player;
    }

    public static async Task<int> EnterObserverPlayingAsync(GameContext observer) {
        PlayerState currentPlayer = (await GetPlayersAsync(observer)).FirstOrDefault(p => p.IsMain && p.HasEntity);
        if (currentPlayer.Exists) {
            return currentPlayer.Index;
        }

        await observer.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool intoPlaying = await observer.WaitLogAsync("Player into playing.", 60);
        Assert.True(intoPlaying, $"Observer failed into playing state. Logs:{Environment.NewLine}{observer.GetRecentLogText()}");

        PlayerState player = await WaitForMainPlayerEntityAsync(observer);
        Assert.True(player.Exists && player.Index >= 0, $"Observer main player entity was not ready. Player={player}.");
        return player.Index;
    }

    public static async Task<PlayerState> WaitForMainPlayerEntityAsync(GameContext context) {
        PlayerState player = default;
        for (int i = 0; i < 60; i++) {
            player = (await GetPlayersAsync(context)).FirstOrDefault(p => p.IsMain && p.HasEntity);
            if (player.Exists) {
                return player;
            }
            await Task.Delay(100);
        }
        return player;
    }

    public static async Task<PlayerState> WaitForPlayerAsync(GameContext context, int playerIndex, string? expectedName = null) {
        PlayerState player = default;
        for (int i = 0; i < 60; i++) {
            player = (await GetPlayersAsync(context)).FirstOrDefault(p => p.Index == playerIndex);
            if (player.Exists && (expectedName == null || player.Name == expectedName)) {
                return player;
            }
            await Task.Delay(100);
        }
        return player;
    }

    public static List<PlayerState> ParsePlayers(string raw) {
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Equals("no-project", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("no-subsystem", StringComparison.OrdinalIgnoreCase)) {
            return [];
        }

        return raw
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParsePlayer)
            .ToList();
    }

    public static PlayerBodyStatusState ParsePlayerBodyStatus(string raw) {
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new PlayerBodyStatusState(false, 0, default, false, raw);
        }

        Dictionary<string, string> values = raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new PlayerBodyStatusState(
            true,
            ParseInt(values, "id"),
            ParseVector2(values["look"]),
            ParseBool(values, "crouch"),
            raw);
    }

    public static bool PlayerBodyStatusNear(PlayerBodyStatusState expected, PlayerBodyStatusState actual) =>
        expected.Exists
        && actual.Exists
        && expected.IsCrouching == actual.IsCrouching
        && Vector2Dto.DistanceSquared(expected.LookAngles, actual.LookAngles) <= LookAnglesEpsilonSquared;

    public static async Task<int> EnsureServerPlayerEntityAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/EnsureServerPlayerEntityCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<InteractionTarget> PrepareUseBoatTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PrepareUseBoatTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task<InteractionTarget> PrepareDoorTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PrepareDoorTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task<InteractionTarget> PreparePlaceTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PreparePlaceTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task<InteractionTarget> PreparePaintBucketTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PreparePaintBucketTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task<InteractionTarget> PrepareDigTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PrepareDigTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task<InteractionTarget> PrepareDropTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PrepareDropTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task<InteractionTarget> PrepareAimTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PrepareAimTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task<InteractionTarget> PrepareHitTargetAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PrepareHitTargetCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        return ParseInteractionTarget(raw);
    }

    public static async Task PrepareClientPlayerForActionAsync(GameContext context, InteractionTarget target, float zOffset = 3f) {
        string result = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/PrepareClientPlayerForActionCode.cs")
                .Replace("__ACTIVE_VALUE__", target.ActiveValue.ToString(CultureInfo.InvariantCulture))
                .Replace("__ACTIVE_COUNT__", target.ActiveCount.ToString(CultureInfo.InvariantCulture))
                .Replace("__BODY_X__", (target.X + 0.5f).ToString("R", CultureInfo.InvariantCulture))
                .Replace("__BODY_Y__", (target.Y + 1f).ToString("R", CultureInfo.InvariantCulture))
                .Replace("__BODY_Z__", (target.Z + zOffset).ToString("R", CultureInfo.InvariantCulture))) ?? "missing";

        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Client player action setup failed on {context.RoleName}. Result={result}. Target={target.Raw}");
        }

        await Task.Delay(150);
    }

    public static async Task InjectInteractInputAsync(GameContext context, InteractionTarget target) {
        string command = string.Create(
            CultureInfo.InvariantCulture,
            $"INTERACT_RAY {target.X + 0.5f:R} {target.Y + 0.5f:R} {target.Z + 2f:R} 0 0 -1");
        await context.PushCommandAsync(command);
        await Task.Delay(250);
    }

    public static async Task InjectHitInputAsync(GameContext context, InteractionTarget target) {
        CameraRay ray = target.EntityId > 0
            ? await GetHitRayToEntityAsync(context, target.EntityId)
            : await GetMainCameraRayAsync(context);
        if (target.EntityId > 0) {
            int raycastEntityId = await GetHitRaycastEntityAsync(context, ray);
            if (raycastEntityId != target.EntityId) {
                throw new InvalidOperationException($"Hit raycast did not hit target on {context.RoleName}. Expected={target.EntityId}, Actual={raycastEntityId}, Ray={ray.Raw}, Target={target.Raw}");
            }
        }

        string command = string.Create(
            CultureInfo.InvariantCulture,
            $"HIT_RAY {ray.Px:R} {ray.Py:R} {ray.Pz:R} {ray.Dx:R} {ray.Dy:R} {ray.Dz:R}");
        await context.PushCommandAsync(command);
        await Task.Delay(300);
    }

    public static async Task InjectDigInputAsync(GameContext context, InteractionTarget target) {
        string command = string.Create(
            CultureInfo.InvariantCulture,
            $"DIG_RAY {target.X + 0.5f:R} {target.Y + 0.5f:R} {target.Z + 2f:R} 0 0 -1 120");
        await context.PushCommandAsync(command);
        await Task.Delay(1500);
    }

    public static async Task InjectAimInputAsync(GameContext context, InteractionTarget target) {
        CameraRay ray = await GetMainCameraRayAsync(context);
        string command = string.Create(
            CultureInfo.InvariantCulture,
            $"AIM_RAY {ray.Px:R} {ray.Py:R} {ray.Pz:R} {ray.Dx:R} {ray.Dy:R} {ray.Dz:R} 80");
        await context.PushCommandAsync(command);
        await Task.Delay(1800);
    }

    public static async Task InjectDropInputAsync(GameContext context) {
        await context.PushCommandAsync("DROP_ONCE");
        await Task.Delay(300);
    }

    public static async Task<CellState> WaitForCellStateAsync(
        GameContext context,
        InteractionTarget target,
        int expectedContents,
        int expectedData) {
        CellState state = default;
        for (int i = 0; i < 100; i++) {
            state = await GetCellStateAsync(context, target);
            if (state.Contents == expectedContents
                && state.Data == expectedData) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    public static async Task<CellState> GetCellStateAsync(GameContext context, InteractionTarget target) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetCellStateCode.cs")
                .Replace("__X__", target.ExpectedX.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y__", target.ExpectedY.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z__", target.ExpectedZ.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;

        return ParseCellState(raw);
    }

    public static async Task<int> WaitForBoatCountAtLeastAsync(GameContext context, InteractionTarget target, int minimumCount) {
        int count = 0;
        for (int i = 0; i < 100; i++) {
            count = await GetBoatCountNearAsync(context, target);
            if (count >= minimumCount) {
                return count;
            }

            await Task.Delay(100);
        }

        return count;
    }

    public static async Task<int> GetBoatCountNearAsync(GameContext context, InteractionTarget target) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetBoatCountNearCode.cs")
                .Replace("__X__", target.ExpectedX.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y__", target.ExpectedY.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z__", target.ExpectedZ.ToString(CultureInfo.InvariantCulture)));

        return result is int count ? count : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<int> WaitForPickableCountAtLeastAsync(GameContext context, InteractionTarget target, int minimumCount) {
        int count = 0;
        for (int i = 0; i < 100; i++) {
            count = await GetPickableCountNearAsync(context, target);
            if (count >= minimumCount) {
                return count;
            }

            await Task.Delay(100);
        }

        return count;
    }

    public static async Task<int> GetPickableCountNearAsync(GameContext context, InteractionTarget target) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetPickableCountNearCode.cs")
                .Replace("__X__", target.ExpectedX.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y__", target.ExpectedY.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z__", target.ExpectedZ.ToString(CultureInfo.InvariantCulture))
                .Replace("__CONTENTS__", target.ExpectedContents.ToString(CultureInfo.InvariantCulture)));

        return result is int count ? count : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<int> WaitForProjectileCountAtLeastAsync(GameContext context, InteractionTarget target, int minimumCount) {
        int count = 0;
        for (int i = 0; i < 100; i++) {
            count = await GetProjectileCountNearAsync(context, target);
            if (count >= minimumCount) {
                return count;
            }

            await Task.Delay(100);
        }

        return count;
    }

    public static async Task<int> GetProjectileCountNearAsync(GameContext context, InteractionTarget target) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetProjectileCountNearCode.cs")
                .Replace("__X__", target.ExpectedX.ToString(CultureInfo.InvariantCulture))
                .Replace("__Y__", target.ExpectedY.ToString(CultureInfo.InvariantCulture))
                .Replace("__Z__", target.ExpectedZ.ToString(CultureInfo.InvariantCulture))
                .Replace("__CONTENTS__", target.ExpectedContents.ToString(CultureInfo.InvariantCulture)));

        return result is int count ? count : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task<CameraRay> GetMainCameraRayAsync(GameContext context) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetMainCameraRayCode.cs")) ?? "missing";

        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new CameraRay(
            ParseFloat(values, "px"),
            ParseFloat(values, "py"),
            ParseFloat(values, "pz"),
            ParseFloat(values, "dx"),
            ParseFloat(values, "dy"),
            ParseFloat(values, "dz"),
            raw);
    }

    public static async Task<CameraRay> GetHitRayToEntityAsync(GameContext context, int entityId) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetHitRayToEntityCode.cs")
                .Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))) ?? "missing";

        Dictionary<string, string> values = ParseKeyValuePairs(raw);
        return new CameraRay(
            ParseFloat(values, "px"),
            ParseFloat(values, "py"),
            ParseFloat(values, "pz"),
            ParseFloat(values, "dx"),
            ParseFloat(values, "dy"),
            ParseFloat(values, "dz"),
            raw);
    }

    public static async Task<int> GetHitRaycastEntityAsync(GameContext context, CameraRay ray) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("PlayerSync/GetHitRaycastEntityCode.cs")
                .Replace("__PX__", ray.Px.ToString("R", CultureInfo.InvariantCulture))
                .Replace("__PY__", ray.Py.ToString("R", CultureInfo.InvariantCulture))
                .Replace("__PZ__", ray.Pz.ToString("R", CultureInfo.InvariantCulture))
                .Replace("__DX__", ray.Dx.ToString("R", CultureInfo.InvariantCulture))
                .Replace("__DY__", ray.Dy.ToString("R", CultureInfo.InvariantCulture))
                .Replace("__DZ__", ray.Dz.ToString("R", CultureInfo.InvariantCulture)));

        if (result is string text && string.Equals(text, "missing", StringComparison.OrdinalIgnoreCase)) {
            return -1;
        }

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static InteractionTarget ParseInteractionTarget(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        return new InteractionTarget(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "expectedX"),
            ParseInt(values, "expectedY"),
            ParseInt(values, "expectedZ"),
            ParseInt(values, "contents"),
            ParseInt(values, "data"),
            ParseInt(values, "expectedContents"),
            ParseInt(values, "expectedData"),
            ParseInt(values, "activeValue"),
            ParseInt(values, "activeCount"),
            ParseInt(values, "entityId"),
            value);
    }

    public static CellState ParseCellState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        return new CellState(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "contents"),
            ParseInt(values, "data"),
            value);
    }

    private static PlayerState ParsePlayer(string raw) {
        Dictionary<string, string> values = raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new PlayerState(
            true,
            ParseInt(values, "index"),
            values.GetValueOrDefault("name", string.Empty),
            values.GetValueOrDefault("skin", string.Empty),
            values.GetValueOrDefault("class", string.Empty),
            ParseBool(values, "isMain"),
            ParseBool(values, "hasEntity"),
            raw);
    }

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : -1;

    private static Vector2Dto ParseVector2(string value) {
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
            return new Vector2Dto(x, y);
        }

        throw new InvalidOperationException($"Invalid Vector2 value: '{value}'.");
    }

    private static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && bool.TryParse(value, out bool result)
        && result;

    public static PlayerRuntimeState ParsePlayerRuntimeState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "no-project", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no-subsystem", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "missing", StringComparison.OrdinalIgnoreCase)) {
            return new PlayerRuntimeState(false, -1, false, 0, 0f, false, -1f, string.Empty, false, 0, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new PlayerRuntimeState(
            true,
            ParseInt(values, "index", -1),
            ParseBool(values, "hasEntity"),
            ParseInt(values, "entityId", 0),
            ParseFloat(values, "level", 0f),
            ParseBool(values, "creativeFly"),
            ParseFloat(values, "health", -1f),
            values.GetValueOrDefault("state", string.Empty),
            ParseBool(values, "readyToRestart"),
            ParseInt(values, "spawns", 0),
            value);
    }

    private static int ParseInt(Dictionary<string, string> values, string key, int defaultValue) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : defaultValue;

    private static float ParseFloat(Dictionary<string, string> values, string key, float defaultValue) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : defaultValue;

    private static float ParseFloat(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;

    private static Dictionary<string, string> ParseKeyValuePairs(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
}

public readonly record struct PlayerState(
    bool Exists,
    int Index,
    string Name,
    string SkinName,
    string PlayerClass,
    bool IsMain,
    bool HasEntity,
    string Raw) {
    public override string ToString() =>
        $"(Exists={Exists}, Index={Index}, Name='{Name}', Skin='{SkinName}', Class='{PlayerClass}', IsMain={IsMain}, HasEntity={HasEntity}, Raw='{Raw}')";
}

public readonly record struct PlayerBodyStatusState(
    bool Exists,
    int Id,
    Vector2Dto LookAngles,
    bool IsCrouching,
    string Raw) {
    public override string ToString() =>
        $"(Exists={Exists}, Id={Id}, LookAngles={LookAngles}, IsCrouching={IsCrouching}, Raw='{Raw}')";
}

public readonly record struct PlayerRuntimeState(
    bool Exists,
    int PlayerIndex,
    bool HasEntity,
    int EntityId,
    float Level,
    bool IsCreativeFlyEnabled,
    float Health,
    string State,
    bool ReadyToRestart,
    int SpawnsCount,
    string Raw) {
    public override string ToString() =>
        $"(Exists={Exists}, PlayerIndex={PlayerIndex}, HasEntity={HasEntity}, EntityId={EntityId}, Level={Level:R}, CreativeFly={IsCreativeFlyEnabled}, Health={Health:R}, State='{State}', ReadyToRestart={ReadyToRestart}, Spawns={SpawnsCount}, Raw='{Raw}')";
}

public readonly record struct InteractionTarget(
    int X,
    int Y,
    int Z,
    int ExpectedX,
    int ExpectedY,
    int ExpectedZ,
    int Contents,
    int Data,
    int ExpectedContents,
    int ExpectedData,
    int ActiveValue,
    int ActiveCount,
    int EntityId,
    string Raw);

public readonly record struct CellState(
    int X,
    int Y,
    int Z,
    int Contents,
    int Data,
    string Raw);

public readonly record struct CameraRay(
    float Px,
    float Py,
    float Pz,
    float Dx,
    float Dy,
    float Dz,
    string Raw);