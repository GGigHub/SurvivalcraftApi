using System;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Silk.NET.OpenAL;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class GameStartupTest : IClassFixture<GameProcessFixture> {
    private readonly GameProcessFixture fixture;
    private GameProcessContext server => fixture.Server;
    private GameProcessContext client => fixture.Client;

    public GameStartupTest(GameProcessFixture fixture) {
        this.fixture = fixture;
    }

    // 串行按顺序执行以下所有测试方法，测试间互相依赖
    // 运行测试时请先编译测试模组
    // 右键测试项目->运行测试
    // 比较考验性能，测试环境r7 1700 + 16G内存
    [Fact, TestOrder(Priority = 1)]
    public async Task TestGameEntersSuccessfully() {
        bool mainScreenEntered =
            await fixture.WaitLogAsync("Entered screen \"MainMenu\"", server);

        Assert.True(mainScreenEntered,
            "Did not receive the target log message within the timeout period.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestWorldCreation() {
        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playScreenEntered = await fixture.WaitLogAsync("Entered screen \"Play\"", server);
        Assert.True(playScreenEntered, "Failed to enter the Play screen");

        await server.PushCommandAsync("CLICK_WIDGET NewWorld");
        bool newWorldScreenEntered = await fixture.WaitLogAsync("Entered screen \"NewWorld\"", server);
        Assert.True(newWorldScreenEntered, "Failed to enter the New World screen");

        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playerScreen = await fixture.WaitLogAsync("Entered screen \"Player\"", server);
        Assert.True(playerScreen, "Failed to load the Player screen");

        await server.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await fixture.WaitLogAsync("Entered screen \"Game\"", server);
        Assert.True(gameEntered, "Failed to enter the Game screen");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestServerConnection() {
        client.Start("--join", $"127.0.0.1:{server.GamePort}");

        bool connected = await fixture.WaitLogAsync($"Connected: 127.0.0.1:{server.GamePort}", client);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await fixture.WaitLogAsync("Entered screen \"Player\"", client);
        Assert.True(playerScreen, "Failed to load the Player screen");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool intoPlaying = await fixture.WaitLogAsync("Player into playing.", client);
        Assert.True(intoPlaying, "Failed into playing state");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestServerReconnection() {
        await client.PushCommandAsync("EXEC " +
            "NetworkManager.Stop(); " +
            "GameManager.SaveProject(true, true); " +
            "GameManager.DisposeProject(); " +
            "ScreensManager.SwitchScreen(\"MainMenu\");");

        bool mainMenuEntered = await fixture.WaitLogAsync("Entered screen \"MainMenu\"", client);
        Assert.True(mainMenuEntered, "Client failed to enter the MainMenu screen after quitting.");

        await client.PushCommandAsync("EXEC " +
            "ScreensManager.SwitchScreen(" +
            $"\"GameLoading\", null, null, System.Net.IPEndPoint.Parse(\"127.0.0.1:{server.GamePort}\"), null);");

        bool gameLoadingEntered = await fixture.WaitLogAsync("Entered screen \"GameLoading\"", client);
        Assert.True(gameLoadingEntered, "Client failed to enter the GameLoading screen during reconnection.");

        bool connected = await fixture.WaitLogAsync($"Connected: 127.0.0.1:{server.GamePort}", client);
        Assert.True(connected, "Client failed to reconnect to the server.");

        bool gameEntered = await fixture.WaitLogAsync("Entered screen \"Game\"", client);
        Assert.True(gameEntered, "Failed to enter the Game screen");

        bool intoPlaying = await fixture.WaitLogAsync("Player into playing.", client);
        Assert.True(intoPlaying, "Failed into playing state");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestServerReconnectionWithServerExitFirst() {
        // 关闭服务器
        await server.PushCommandAsync("EXEC " +
            "NetworkManager.Stop(); " +
            "GameManager.SaveProject(true, true); " +
            "GameManager.DisposeProject(); " +
            "ScreensManager.SwitchScreen(\"MainMenu\");");

        // 等待客户端日志以确认对话框已显示
        bool disconnectDialogShown = await fixture.WaitLogAsync("Dialog shown: Disconnected", client);
        Assert.True(disconnectDialogShown, "Client did not show the Disconnected dialog.");

        await server.PushCommandAsync("EXEC " +
            "Game.WorldsManager.UpdateWorldsList(); " +
            "Game.NetworkManager.StartServer(); " +
            "Game.ScreensManager.SwitchScreen(\"GameLoading\", Game.WorldsManager.WorldInfos[0], null);");

        await client.PushCommandAsync("EXEC DialogsManager.HideAllDialogs();");
        await client.PushCommandAsync("EXEC " +
            "ScreensManager.SwitchScreen(" +
            $"\"GameLoading\", null, null, System.Net.IPEndPoint.Parse(\"127.0.0.1:{server.GamePort}\"), null);");
        
        bool clientGameEntered = await fixture.WaitLogAsync("Entered screen \"Game\"", client);
        Assert.True(clientGameEntered, "Client failed to enter the Game screen");

        bool clientIntoPlaying = await fixture.WaitLogAsync("Player into playing.", client);
        Assert.True(clientIntoPlaying, "Client failed into playing state");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
