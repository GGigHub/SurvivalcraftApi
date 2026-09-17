using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

internal static class NeorxnaSyncTools {
    public static async Task<int> EnsureWorldAndClientAsync(GameContext server, GameContext client, string seed) {
        if (!(await IsProjectReadyAsync(server))) {
            await PlayerSyncTools.CreateCreativeTestWorldAsync(server, seed);
        }
        if (!(await IsProjectReadyAsync(client))) {
            await GameTools.ConnectTestServer(client, server.GamePort);
        }

        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);
        return playerIndex;
    }

    public static async Task<bool> IsProjectReadyAsync(GameContext context) {
        object result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
        return result is bool b && b;
    }

    // Dash drains Stamina/Food, which the vanilla vital-stats simulation resets
    // every frame in Creative mode. Use a Survival world so the drain is real.
    public static async Task CreateSurvivalTestWorldAsync(GameContext server, string seed) {
        await GameTools.DisablePlayWithFriendsPrompt(server);
        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playScreenEntered = await server.WaitLogAsync("Entered screen \"Play\"");
        Assert.True(playScreenEntered, "Failed to enter the Play screen");

        await server.PushCommandAsync("CLICK_WIDGET NewWorld");
        bool newWorldScreenEntered = await server.WaitLogAsync("Entered screen \"NewWorld\"");
        Assert.True(newWorldScreenEntered, "Failed to enter the New World screen");

        for (int i = 0; i < 50; i++) {
            object? isReady = await server.PushCommandAsync("FUNC return (ScreensManager.CurrentScreen as NewWorldScreen)?.m_worldSettings != null;");
            if (isReady is bool ready && ready) {
                break;
            }

            await Task.Delay(100);
        }

        await server.PushCommandAsync($"EXEC (ScreensManager.CurrentScreen as NewWorldScreen).m_seedTextBox.Text = \"{seed}\";");

        string modeResult = string.Empty;
        for (int i = 0; i < 50; i++) {
            modeResult = (string?)await server.PushCommandAsync("FUNC " + GameTools.ReadScript("PlayerSync/SetNewWorldModeCode.cs")
                .Replace("__GAME_MODE__", "Survival")) ?? string.Empty;
            if (string.Equals(modeResult, "Survival", StringComparison.Ordinal)) {
                break;
            }

            await Task.Delay(100);
        }

        Assert.Equal("Survival", modeResult);

        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playerScreen = await server.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen");

        await server.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await server.WaitLogAsync("Entered screen \"Game\"");
        Assert.True(gameEntered, "Failed to enter the Game screen");
    }

    public static async Task<DashState> GetDashStateAsync(GameContext context, int playerIndex) {
        string raw = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/GetDashStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
        return DashState.Parse(raw);
    }

    public static async Task<string> PrepareDashAsync(GameContext context) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/PrepareDashCode.cs")) ?? string.Empty;
    }

    public static async Task<string> PrimeDashAsync(GameContext context, int playerIndex) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/PrimeDashCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
    }

    public static async Task<string> ShowRecipeDialogAsync(GameContext context) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/ShowRecipeDialogCode.cs")) ?? string.Empty;
    }

    public static async Task<string> ForceWindowActiveAsync(GameContext context) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/ForceWindowActiveCode.cs")) ?? string.Empty;
    }

    public static async Task ReconnectClientAsync(GameContext client, int serverPort) {
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString()));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}", 45);
        Assert.True(connected, $"客户端重连失败（未连接）。Logs:{Environment.NewLine}{client.GetRecentLogText()}");

        bool gameEntered = await client.WaitLogAsync("Entered screen \"Game\"", 45);
        Assert.True(gameEntered, $"客户端重连后未进入 Game 界面。Logs:{Environment.NewLine}{client.GetRecentLogText()}");
    }

    public static async Task<string> GetRespawnDataAsync(GameContext context) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/GetRespawnDataCode.cs")) ?? string.Empty;
    }

    public static async Task<string> WaitUntilRespawnDataHasBackPositionAsync(GameContext context, int playerIndex) {
        WaitResult<string> result = await TestWaiter.TryUntilAsync(
            () => GetRespawnDataAsync(context),
            data => data.Contains($"backs={playerIndex}=", StringComparison.OrdinalIgnoreCase)
                || data.Contains($"backs=;{playerIndex}=", StringComparison.OrdinalIgnoreCase)
                || data.Contains("backs=" + playerIndex + "=", StringComparison.Ordinal),
            TestTimeouts.DefaultEventually);
        return result.Last;
    }

    public static async Task<string> SetPlayerHomeAsync(GameContext context, int playerIndex, string homeName) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/SetPlayerHomeCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__HOME_NAME__", homeName)) ?? string.Empty;
    }

    public static async Task<string> GetDebugDrawingStateAsync(GameContext context, int playerIndex) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/GetDebugDrawingStateCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? string.Empty;
    }

    public static async Task<string> GetRecipeDialogStateAsync(GameContext context) {
        return (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/GetRecipeDialogStateCode.cs")) ?? string.Empty;
    }

    public static async Task<RecipeDialogState> WaitForRecipeDialogAsync(GameContext context) {
        WaitResult<string> result = await TestWaiter.TryUntilAsync(
            () => GetRecipeDialogStateAsync(context),
            state => RecipeDialogState.Parse(state).Exists,
            TestTimeouts.DefaultEventually);
        return RecipeDialogState.Parse(result.Last);
    }

    public static async Task SeedMainPlayerInventoryAsync(GameContext context) {
        string result = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/SeedMainPlayerInventoryCode.cs")) ?? string.Empty;
        Assert.Equal("seeded", result);
    }

    public static async Task OpenInventoryAsync(GameContext context) {
        await context.PushCommandAsync("KEYDOWN E");
        bool opened = (await TestWaiter.TryUntilAsync(
            async () => await context.PushCommandAsync(
                "FUNC return Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().PlayersData.Any(p => p.IsMainPlayer && p.ComponentPlayer?.ComponentGui?.ModalPanelWidget != null);") is bool value && value,
            value => value,
            TestTimeouts.DefaultEventually)).Matched;
        await context.PushCommandAsync("KEYUP E");
        Assert.True(opened, $"{context.RoleName} 未能打开物品栏。");
    }

    public static async Task MoveMouseToRecipeSlotAsync(GameContext context, string slotResult) {
        string[] parts = slotResult.Split(':');
        int x = 0;
        int y = 0;
        bool valid = parts.Length == 4
            && (parts[0] == "armed" || parts[0] == "slot")
            && int.TryParse(parts[2], out x)
            && int.TryParse(parts[3], out y);
        Assert.True(valid, $"{context.RoleName} 未能定位 Neorxna 配方物品格，结果={slotResult}");

        await context.PushCommandAsync($"MOUSEMOVE {x} {y}");
        bool visible = (await TestWaiter.TryUntilAsync(
            async () => await context.PushCommandAsync(
                "FUNC var cursor = Neorxna.NUI.NUIManager.CursorContentWidget; return cursor?.IsVisible == true && cursor.GetWidget() is Neorxna.NUI.NUIBlockCursorContent;") is bool value && value,
            value => value,
            TestTimeouts.DefaultEventually)).Matched;
        Assert.True(visible, $"{context.RoleName} 移到物品格后未构造 Neorxna 光标内容。");
    }
}

internal readonly record struct DashState(
    bool Exists,
    bool Dashing,
    bool Enabled,
    float Food,
    float Stamina,
    string FactorNames,
    string Raw) {
    public string Move => GetValue("move");
    public string WindowActive => GetValue("windowActive");
    public string KeyFDown => GetValue("keyF");
    public string BoolSettings => GetValue("bools");

    public static DashState Parse(string raw) {
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Equals("no-project", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("no-player", StringComparison.OrdinalIgnoreCase)) {
            return new DashState(false, false, false, -1f, -1f, string.Empty, raw);
        }

        var values = raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new DashState(
            true,
            ParseBool(values, "dashing"),
            ParseBool(values, "enabled"),
            ParseFloat(values, "food", -1f),
            ParseFloat(values, "stamina", -1f),
            values.GetValueOrDefault("factors", string.Empty),
            raw);
    }

    public override string ToString() =>
        $"(Dashing={Dashing}, Enabled={Enabled}, Food={Food:R}, Stamina={Stamina:R}, Factors='{FactorNames}', Move={Move}, WindowActive={WindowActive}, KeyFDown={KeyFDown}, Bools='{BoolSettings}')";

    private string GetValue(string key) {
        var values = Raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        return values.GetValueOrDefault(key, string.Empty);
    }

    private static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && bool.TryParse(value, out bool result) && result;

    private static float ParseFloat(Dictionary<string, string> values, string key, float defaultValue) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : defaultValue;
}

internal readonly record struct RecipeDialogState(
    bool MainPlayerFound,
    bool MainGuiFound,
    bool Exists,
    bool ParentIsMainGui,
    bool DialogVisible,
    bool DialogVisibleGlobal,
    bool MainGuiVisibleGlobal,
    string Raw) {
    public static RecipeDialogState Parse(string state) {
        string[] parts = state.Split(';');
        return new RecipeDialogState(
            parts.Length > 0 && parts[0] == "True",
            parts.Length > 1 && parts[1] == "True",
            parts.Length > 2 && parts[2] == "True",
            parts.Length > 3 && parts[3] == "True",
            parts.Length > 4 && parts[4] == "True",
            parts.Length > 5 && parts[5] == "True",
            parts.Length > 6 && parts[6] == "True",
            state);
    }
}
