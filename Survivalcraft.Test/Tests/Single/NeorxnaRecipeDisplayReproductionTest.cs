using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

// Neorxna and CompatNet are installed into both game processes. Both tests are
// expected to pass after CompatNet redirects Neorxna's recipe dialog parent.
public sealed class NeorxnaGameFixture : GameFixture {
    private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_NEORXNA_MOD_PATH";
    private const string DefaultModFileName = "01219_[API1.9.2.1]Neorxna-v1.4.scmod";
    private const string CompatNetModFileName = "Survivalcraft.CompatNet.scmod";

    protected override void ConfigureContext(GameContext context) {
        context.InstallMod(FindModPackage());
        context.InstallMod(FindCompatNetPackage());
    }

    private static string FindModPackage() {
        string? configuredPath = Environment.GetEnvironmentVariable(ModPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath)) {
            return configuredPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "NF3",
            "tools",
            "CommunityMods",
            DefaultModFileName);
    }

    private static string FindCompatNetPackage() {
        return Path.Combine(AppContext.BaseDirectory, "Mods", CompatNetModFileName);
    }
}

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class NeorxnaRecipeDisplayReproductionTest : IClassFixture<NeorxnaGameFixture>, IDisposable {
    private readonly NeorxnaGameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public NeorxnaRecipeDisplayReproductionTest(NeorxnaGameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task ServerDisplaysNeorxnaRecipesAfterPressingU() {
        await GameTools.CreateTestWorld(server, "neorxna-recipe-display");
        await AssertNeorxnaInstallationAsync(server, expected: true);
        await AssertCompatNetInstallationAsync(server, expected: true);
        await GameTools.ConnectTestServer(client, server.GamePort);
        await AssertNeorxnaInstallationAsync(client, expected: true);
        await AssertCompatNetInstallationAsync(client, expected: true);
        await SeedMainPlayerInventoryAsync(server);
        await SeedMainPlayerInventoryAsync(client);
        await OpenInventoryAsync(server);
        await OpenInventoryAsync(client);

        string armResult = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/PrepareRecipeCursorCode.cs")) ?? string.Empty;
        await MoveMouseToRecipeSlotAsync(server, armResult, true);

        await server.PushCommandAsync("KEYDOWN U");
        RecipeDialogState state = await WaitForRecipeDialogAsync(server);
        await server.PushCommandAsync("KEYUP U");

        AssertRecipeDialogIsDisplayed("服务端", state);
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task ClientDisplaysNeorxnaRecipesAfterPressingU() {
        await AssertNeorxnaInstallationAsync(client, expected: true);
        await AssertCompatNetInstallationAsync(client, expected: true);
        string slotResult = (string?)await client.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/LocateInventorySlotCode.cs")) ?? string.Empty;
        await MoveMouseToRecipeSlotAsync(client, slotResult, true);

        await client.PushCommandAsync("KEYDOWN U");
        RecipeDialogState state = await WaitForRecipeDialogAsync(client);
        await client.PushCommandAsync("KEYUP U");

        AssertRecipeDialogIsDisplayed("客户端", state);
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private static async Task<RecipeDialogState> WaitForRecipeDialogAsync(GameContext context) {
        WaitResult<string> result = await TestWaiter.TryUntilAsync(
            () => ReadRecipeDialogStateAsync(context),
            state => ParseRecipeDialogState(state).Exists,
            TestTimeouts.DefaultEventually);
        return ParseRecipeDialogState(result.Last);
    }

    private static async Task<string> ReadRecipeDialogStateAsync(GameContext context) {
        return (string?)await context.PushCommandAsync(
            "FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().PlayersData; "
            + "var main = players.FirstOrDefault(p => p.IsMainPlayer)?.ComponentPlayer; "
            + "var dialog = DialogsManager.Dialogs.LastOrDefault(d => d.GetType().FullName == \"Neorxna.NUI.NUIRecipaediaRecipesDialog\"); "
            + "return string.Join(\";\", new object[] { main != null, main?.GuiWidget != null, dialog != null, dialog?.ParentWidget == main?.GuiWidget, dialog?.IsVisible == true, dialog?.IsVisibleGlobal == true, main?.GuiWidget?.IsVisibleGlobal == true });") ?? string.Empty;
    }

    private static RecipeDialogState ParseRecipeDialogState(string state) {
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

    private static void AssertRecipeDialogIsDisplayed(string role, RecipeDialogState state) {
        Assert.True(state.MainPlayerFound, $"{role} 未找到本地玩家。状态={state.Raw}");
        Assert.True(state.MainGuiFound, $"{role} 未找到本地玩家 GUI。状态={state.Raw}");
        Assert.True(state.Exists, $"{role} 按 U 后没有创建 Neorxna 配方对话框。状态={state.Raw}");
        Assert.True(state.ParentIsMainGui, $"{role} 的 Neorxna 配方对话框没有挂在本地玩家 GUI 上。状态={state.Raw}");
        Assert.True(state.DialogVisible, $"{role} 的 Neorxna 配方对话框自身不可见。状态={state.Raw}");
        Assert.True(state.DialogVisibleGlobal, $"{role} 的 Neorxna 配方对话框不在可见的 GUI 层级中。状态={state.Raw}");
        Assert.True(state.MainGuiVisibleGlobal, $"{role} 的本地玩家 GUI 不可见。状态={state.Raw}");
    }

    private static async Task SeedMainPlayerInventoryAsync(GameContext context) {
        string result = (string?)await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("Neorxna/SeedMainPlayerInventoryCode.cs")) ?? string.Empty;
        Assert.Equal("seeded", result);
    }

    private static async Task AssertNeorxnaInstallationAsync(GameContext context, bool expected) {
        object? result = await context.PushCommandAsync(
            "FUNC return ModsManager.ModList.Any(m => m.modInfo?.PackageName == \"ysf.neorxna\");");
        Assert.Equal(expected, result);
    }

    private static async Task AssertCompatNetInstallationAsync(GameContext context, bool expected) {
        object? result = await context.PushCommandAsync(
            "FUNC return ModsManager.ModList.Any(m => m.modInfo?.PackageName == \"compatnet\");");
        Assert.Equal(expected, result);
    }

    private static async Task OpenInventoryAsync(GameContext context) {
        await context.PushCommandAsync("KEYDOWN E");
        bool opened = (await TestWaiter.TryUntilAsync(
            async () => await context.PushCommandAsync(
                "FUNC return Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().PlayersData.Any(p => p.IsMainPlayer && p.ComponentPlayer?.ComponentGui?.ModalPanelWidget != null);") is bool value && value,
            value => value,
            TestTimeouts.DefaultEventually)).Matched;
        await context.PushCommandAsync("KEYUP E");
        Assert.True(opened, $"{context.RoleName} 未能打开物品栏。");
    }

    private static async Task MoveMouseToRecipeSlotAsync(GameContext context, string slotResult, bool expectNeorxnaContent) {
        string[] parts = slotResult.Split(':');
        int x = 0;
        int y = 0;
        bool valid = parts.Length == 4
            && (parts[0] == "armed" || parts[0] == "slot")
            && int.TryParse(parts[2], out x)
            && int.TryParse(parts[3], out y);
        Assert.True(valid,
            $"{context.RoleName} 未能定位 Neorxna 配方物品格，结果={slotResult}");

        await context.PushCommandAsync($"MOUSEMOVE {x} {y}");
        if (!expectNeorxnaContent) {
            return;
        }

        bool visible = (await TestWaiter.TryUntilAsync(
            async () => await context.PushCommandAsync(
                "FUNC var cursor = Neorxna.NUI.NUIManager.CursorContentWidget; return cursor?.IsVisible == true && cursor.GetWidget() is Neorxna.NUI.NUIBlockCursorContent;") is bool value && value,
            value => value,
            TestTimeouts.DefaultEventually)).Matched;
        Assert.True(visible, $"{context.RoleName} 移到物品格后未构造 Neorxna 光标内容。");
    }

    private readonly record struct RecipeDialogState(
        bool MainPlayerFound,
        bool MainGuiFound,
        bool Exists,
        bool ParentIsMainGui,
        bool DialogVisible,
        bool DialogVisibleGlobal,
        bool MainGuiVisibleGlobal,
        string Raw);
}
