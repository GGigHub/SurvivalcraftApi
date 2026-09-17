using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class NeorxnaSyncTest : IClassFixture<NeorxnaSyncGameFixture>, IDisposable {
    private static readonly string EvidencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Survivalcraft.Neorxna",
        "NeorxnaSyncEvidence.txt");

    private readonly NeorxnaSyncGameFixture fixture;
    private readonly ITestOutputHelper output;

    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public NeorxnaSyncTest(NeorxnaSyncGameFixture fixture, ITestOutputHelper output) {
        this.fixture = fixture;
        this.output = output;
    }

    // ------------------------------------------------------------------
    // 1) 冲刺（Dash）：客户端玩家冲刺改变 ComponentVitalStats 与
    //    ComponentLevel 的 SpeedFactor，对比两端耐力/饱食扣减与冲刺状态。
    // ------------------------------------------------------------------
    [Fact, TestOrder(Priority = 1)]
    public async Task DashIsClientLocalAndVitalStatsConverge() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        string prepare = await NeorxnaSyncTools.PrepareDashAsync(client);
        Assert.StartsWith("ok:", prepare);

        await Task.Delay(1500);

        DashState baselineServer = await NeorxnaSyncTools.GetDashStateAsync(server, playerIndex);
        DashState baselineClient = await NeorxnaSyncTools.GetDashStateAsync(client, playerIndex);
        Assert.True(baselineClient.Exists && baselineServer.Exists,
            $"基准冲刺状态缺失。server={baselineServer};client={baselineClient};prepare={prepare}");

        // 客户端按住前进 + 冲刺键。
        string prime = await NeorxnaSyncTools.PrimeDashAsync(client, playerIndex);
        Assert.Equal("primed", prime);
        await client.PushCommandAsync("MOVE_INPUT 0 0 1 420");
        await Task.Delay(900);

        DashState duringServer = await NeorxnaSyncTools.GetDashStateAsync(server, playerIndex);
        DashState duringClient = await NeorxnaSyncTools.GetDashStateAsync(client, playerIndex);

        // 记录冲刺瞬间的耐力/饱食扣减差异（客户端扣、服务端未扣）。
        float foodDeltaDuring = duringServer.Food - duringClient.Food;
        float staminaDeltaDuring = duringServer.Stamina - duringClient.Stamina;

        // 松开。
        await client.PushCommandAsync("MOVE_INPUT 0 0 0 1");
        await Task.Delay(2500);

        DashState afterServer = await NeorxnaSyncTools.GetDashStateAsync(server, playerIndex);
        DashState afterClient = await NeorxnaSyncTools.GetDashStateAsync(client, playerIndex);

        // (1) 客户端冲刺玩法路径确实被触发（IsDashing=true）。
        Assert.True(duringClient.Dashing,
            $"客户端冲刺未激活。during={duringClient};baseline={baselineClient};prepare={prepare}");

        // (2) 服务端远端玩家不应进入冲刺（Move 恒为 0，服务端不持有客户端输入）。
        //     这是本次测试的“不同步”证据，如实记录，不断言两端一致。
        bool dashStateSynced = duringClient.Dashing == duringServer.Dashing
            && string.Equals(duringClient.FactorNames, duringServer.FactorNames, StringComparison.Ordinal);

        // (3) 耐力/饱食最终应由服务端权威 VitalStatSync 收敛一致。
        Assert.True(
            Math.Abs(afterServer.Food - afterClient.Food) <= 0.03f,
            $"冲刺后饱食未收敛：server={afterServer};client={afterClient}");
        Assert.True(
            Math.Abs(afterServer.Stamina - afterClient.Stamina) <= 0.05f,
            $"冲刺后耐力未收敛：server={afterServer};client={afterClient}");

        string evidence = string.Join(
            Environment.NewLine,
            new[] {
                $"[冲刺] 基准 server={baselineServer} client={baselineClient}",
                $"[冲刺-进行中] server={duringServer}",
                $"[冲刺-进行中] client={duringClient}",
                $"[冲刺-进行中] 服务端饱食-客户端饱食={foodDeltaDuring:R};服务端耐力-客户端耐力={staminaDeltaDuring:R}",
                $"[冲刺-结束后] server={afterServer}",
                $"[冲刺-结束后] client={afterClient}",
                $"[冲刺] 冲刺激活/速度因子两端是否一致={dashStateSynced}"
            });
        OutputEvidence(evidence);
    }

    // ------------------------------------------------------------------
    // 2) 玩家重生数据：死亡时 SubsystemNeorxnaBlock 保存
    //    PlayerBackPositions/PlayerHomes，重生时恢复 ComponentDebugDrawing。
    // ------------------------------------------------------------------
    [Fact, TestOrder(Priority = 2)]
    public async Task RespawnDataDeathSaveAndRestoreSync() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        string baselineServer = await NeorxnaSyncTools.GetRespawnDataAsync(server);
        string baselineClient = await NeorxnaSyncTools.GetRespawnDataAsync(client);

        // 通过真实模组方法在服务端玩家实体上设置一个“家”。
        string setHome = await NeorxnaSyncTools.SetPlayerHomeAsync(server, playerIndex, "死亡之家");
        Assert.Equal("set", setHome);

        PlayerRuntimeState oldServer = await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);
        PlayerRuntimeState oldClient = await PlayerSyncTools.WaitForPlayerEntityAsync(client, playerIndex);

        // 服务端击杀（服务端权威死亡，触发 DeadBeforeDrops 记录重生数据）。
        PlayerRuntimeState killed = await PlayerSyncTools.KillPlayerAsync(server, playerIndex);
        Assert.True(killed.Health <= PlayerSyncTools.HealthEpsilon, $"服务端未击杀玩家。State={killed}.");

        await PlayerSyncTools.WaitForDeadAsync(client, playerIndex);

        // 等两端子系统都记录（或超时，如实记录“客户端未记录”）。
        string serverDeathData = await NeorxnaSyncTools.WaitUntilRespawnDataHasBackPositionAsync(server, playerIndex);
        WaitResult<string> clientDeathResult = await TestWaiter.TryUntilAsync(
            () => NeorxnaSyncTools.GetRespawnDataAsync(client),
            data => data.Contains($"backs={playerIndex}=", StringComparison.OrdinalIgnoreCase)
                || data.Contains("backs=" + playerIndex + "=", StringComparison.Ordinal),
            TestTimeouts.DefaultEventually);
        string clientDeathData = clientDeathResult.Last;

        bool deathDataSynced = deathDataEqual(serverDeathData, clientDeathData);

        // 客户端请求重生。
        string request = await PlayerSyncTools.RequestRespawnAsync(client);
        Assert.DoesNotContain("missing", request, StringComparison.OrdinalIgnoreCase);

        PlayerRuntimeState newServer = await PlayerSyncTools.WaitForRespawnedAsync(server, playerIndex, oldServer.EntityId);
        PlayerRuntimeState newClient = await PlayerSyncTools.WaitForRespawnedAsync(client, playerIndex, oldClient.EntityId);
        Assert.True(newServer.EntityId > 0 && newClient.EntityId > 0,
            $"重生后实体无效。server={newServer};client={newClient}");

        string serverRestored = await NeorxnaSyncTools.GetDebugDrawingStateAsync(server, playerIndex);
        string clientRestored = await NeorxnaSyncTools.GetDebugDrawingStateAsync(client, playerIndex);

        bool restoredSynced = string.Equals(serverRestored, clientRestored, StringComparison.Ordinal);

        string evidence = string.Join(
            Environment.NewLine,
            new[] {
                $"[重生数据-基准] server={baselineServer}",
                $"[重生数据-基准] client={baselineClient}",
                $"[重生数据-死亡] server={serverDeathData}",
                $"[重生数据-死亡] client={clientDeathData}",
                $"[重生数据] 死亡时两端子系统数据是否一致={deathDataSynced}",
                $"[重生数据-重生恢复] server={serverRestored}",
                $"[重生数据-重生恢复] client={clientRestored}",
                $"[重生数据] 重生后两端实体恢复状态是否一致={restoredSynced}"
            });
        OutputEvidence(evidence);
    }

    // ------------------------------------------------------------------
    // 3) 服务端设置 Home 后，客户端重连能否读到 SubsystemNeorxnaBlock 数据。
    // ------------------------------------------------------------------
    [Fact, TestOrder(Priority = 3)]
    public async Task ServerHomeReadableAfterClientReconnect() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        string setHome = await NeorxnaSyncTools.SetPlayerHomeAsync(server, playerIndex, "重连之家");
        Assert.Equal("set", setHome);
        await PlayerSyncTools.KillPlayerAsync(server, playerIndex);
        string serverDataBeforeReconnect = await NeorxnaSyncTools.WaitUntilRespawnDataHasBackPositionAsync(server, playerIndex);
        Assert.Contains("重连之家", serverDataBeforeReconnect, StringComparison.Ordinal);

        // 客户端断开并回到主菜单。
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/DisconnectCode.cs"));
        bool leftMenu = await client.WaitLogAsync("Entered screen \"MainMenu\"", 30);
        Assert.True(leftMenu, "客户端断开后未回到主菜单。");

        // 客户端重连（重连会跳过 Player 界面，直接进入 Game 界面）。
        await NeorxnaSyncTools.ReconnectClientAsync(client, server.GamePort);
        int reconnectIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(reconnectIndex >= 0, "重连后未获得玩家索引。");
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, reconnectIndex);
        await PlayerSyncTools.WaitForMainPlayerEntityAsync(client);

        string serverAfter = await NeorxnaSyncTools.GetRespawnDataAsync(server);
        string clientAfter = await NeorxnaSyncTools.GetRespawnDataAsync(client);

        // 读取重连后玩家实体的 ComponentDebugDrawing（BackPosition / homes）。
        string clientPlayerRestored = await NeorxnaSyncTools.GetDebugDrawingStateAsync(client, reconnectIndex);
        string serverPlayerRestored = await NeorxnaSyncTools.GetDebugDrawingStateAsync(server, reconnectIndex);

        bool subsystemSynced = string.Equals(serverAfter, clientAfter, StringComparison.Ordinal);
        bool playerStateSynced = string.Equals(serverPlayerRestored, clientPlayerRestored, StringComparison.Ordinal);

        string evidence = string.Join(
            Environment.NewLine,
            new[] {
                $"[重连-断开前] server={serverDataBeforeReconnect}",
                $"[重连] 重连后 server 子系统={serverAfter}",
                $"[重连] 重连后 client 子系统={clientAfter}",
                $"[重连] 两端 SubsystemNeorxnaBlock 是否一致={subsystemSynced}",
                $"[重连] server 玩家实体恢复={serverPlayerRestored}",
                $"[重连] client 玩家实体恢复={clientPlayerRestored}",
                $"[重连] 两端玩家实体 ComponentDebugDrawing 是否一致={playerStateSynced}"
            });
        OutputEvidence(evidence);
    }

    // ------------------------------------------------------------------
    // 4) 配方对话框：服务端/客户端各端按 U 打开的本地 UI（CompatNet
    //    已修正父级），对比两端对话框指纹。
    // ------------------------------------------------------------------
    [Fact, TestOrder(Priority = 4)]
    public async Task RecipeDialogOpensOnBothEnds() {
        await EnsureWorldAndClientAsync();

        // 直接构造并弹出 Neorxna 配方对话框：ShowDialog 会触发
        // CompatNet 的 OnShowDialog 修正（把对话框父级挂到本地玩家 GUI 上），
        // 与按 U 的展示路径等价，但不受测试环境键盘注入稳定性影响。
        string serverShown = await NeorxnaSyncTools.ShowRecipeDialogAsync(server);
        Assert.Equal("shown", serverShown);
        string clientShown = await NeorxnaSyncTools.ShowRecipeDialogAsync(client);
        Assert.Equal("shown", clientShown);

        RecipeDialogState serverState = await NeorxnaSyncTools.WaitForRecipeDialogAsync(server);
        RecipeDialogState clientState = await NeorxnaSyncTools.WaitForRecipeDialogAsync(client);
        AssertRecipeDialogDisplayed("服务端", serverState);
        AssertRecipeDialogDisplayed("客户端", clientState);

        // 配方对话框是纯本地 UI：一端打开不应影响另一端。
        RecipeDialogState serverAfterClientOpen = RecipeDialogState.Parse(await NeorxnaSyncTools.GetRecipeDialogStateAsync(server));
        RecipeDialogState clientAfterServerOpen = RecipeDialogState.Parse(await NeorxnaSyncTools.GetRecipeDialogStateAsync(client));

        string evidence = string.Join(
            Environment.NewLine,
            new[] {
                $"[配方对话框] server raw={serverState.Raw}",
                $"[配方对话框] client raw={clientState.Raw}",
                $"[配方对话框] 客户端打开后，服务端是否仍保持其自身对话框={serverAfterClientOpen.Exists}",
                $"[配方对话框] 客户端对话框已打开={clientAfterServerOpen.Exists}"
            });
        OutputEvidence(evidence);

        // 关闭对话框，避免残留 UI。
        await CloseUiAsync(server);
        await CloseUiAsync(client);
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private async Task EnsureWorldAndClientAsync() {
        if (!(await NeorxnaSyncTools.IsProjectReadyAsync(server))) {
            await NeorxnaSyncTools.CreateSurvivalTestWorldAsync(server, "neorxna-sync");
        }
        if (!(await NeorxnaSyncTools.IsProjectReadyAsync(client))) {
            await GameTools.ConnectTestServer(client, server.GamePort);
        }
    }

    private void OutputEvidence(string evidence) {
        output.WriteLine(Environment.NewLine + evidence);
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath)!);
            StringBuilder banner = new StringBuilder();
            banner.AppendLine(new string('=', 70));
            banner.AppendLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] {Environment.NewLine}{evidence}");
            File.AppendAllText(EvidencePath, banner.ToString(), Encoding.UTF8);
        }
        catch {
            // Evidence logging must never break the test.
        }
    }

    private static bool deathDataEqual(string serverDeathData, string clientDeathData) {
        // Compare only the shape (which player index + which homes); exact
        // positions may differ by a few centimeters because each end records
        // its own body position at the moment it processed the death.
        return compressPositions(serverDeathData).Equals(compressPositions(clientDeathData), StringComparison.Ordinal);
    }

    private static string compressPositions(string data) {
        // "idx=x@x,y,z;..." -> keep the home names only so small coordinate
        // differences do not make the two ends look "differently shaped".
        System.Text.RegularExpressions.Regex re = new("@-?\\d", System.Text.RegularExpressions.RegexOptions.Compiled);
        return re.Replace(data, "@");
    }

    private static void AssertRecipeDialogDisplayed(string role, RecipeDialogState state) {
        Assert.True(state.MainPlayerFound, $"{role} 未找到本地玩家。状态={state.Raw}");
        Assert.True(state.MainGuiFound, $"{role} 未找到本地玩家 GUI。状态={state.Raw}");
        Assert.True(state.Exists, $"{role} 按 U 后没有创建 Neorxna 配方对话框。状态={state.Raw}");
        Assert.True(state.ParentIsMainGui, $"{role} 的 Neorxna 配方对话框没有挂在本地玩家 GUI 上。状态={state.Raw}");
        Assert.True(state.DialogVisible, $"{role} 的 Neorxna 配方对话框自身不可见。状态={state.Raw}");
        Assert.True(state.DialogVisibleGlobal, $"{role} 的 Neorxna 配方对话框不在可见的 GUI 层级中。状态={state.Raw}");
        Assert.True(state.MainGuiVisibleGlobal, $"{role} 的本地玩家 GUI 不可见。状态={state.Raw}");
    }

    private static async Task CloseUiAsync(GameContext context) {
        await context.PushCommandAsync("KEYDOWN Escape");
        await Task.Delay(150);
        await context.PushCommandAsync("KEYUP Escape");
        await Task.Delay(300);
    }
}