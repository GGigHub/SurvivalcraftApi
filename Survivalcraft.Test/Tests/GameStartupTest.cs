using System;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Silk.NET.OpenAL;
using Xunit;
#pragma warning disable CS8618

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class GameStartupTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public GameStartupTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    // 串行按顺序执行以下所有测试方法，测试间互相依赖
    // 运行测试时请先编译测试模组
    // 右键测试项目->运行测试
    // 比较考验性能，测试环境r7 1700 + 16G内存
    [Fact, TestOrder(Priority = 1)]
    public async Task TestWorldCreation() {
        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playScreenEntered = await server.WaitLogAsync("Entered screen \"Play\"");
        Assert.True(playScreenEntered, "Failed to enter the Play screen");

        await server.PushCommandAsync("CLICK_WIDGET NewWorld");
        bool newWorldScreenEntered = await server.WaitLogAsync("Entered screen \"NewWorld\"");
        Assert.True(newWorldScreenEntered, "Failed to enter the New World screen");

        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playerScreen = await server.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen");

        await server.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await server.WaitLogAsync("Entered screen \"Game\"");
        Assert.True(gameEntered, "Failed to enter the Game screen");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestServerConnection() {
        await client.PushCommandAsync("EXEC " + 
            GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs")
            .Replace("{0}", server.GamePort.ToString()));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{server.GamePort}");
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool intoPlaying = await client.WaitLogAsync("Player into playing.");
        Assert.True(intoPlaying, "Failed into playing state");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestServerReconnection() {
        await client.PushCommandAsync("EXEC " + 
            GameTools.ReadScript("GameStartup/DisconnectCode.cs"));

        bool mainMenuEntered = await client.WaitLogAsync("Entered screen \"MainMenu\"");
        Assert.True(mainMenuEntered, "Client failed to enter the MainMenu screen after quitting.");

        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", server.GamePort.ToString()));

        bool gameLoadingEntered = await client.WaitLogAsync("Entered screen \"GameLoading\"");
        Assert.True(gameLoadingEntered, "Client failed to enter the GameLoading screen during reconnection.");

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{server.GamePort}");
        Assert.True(connected, "Client failed to reconnect to the server.");

        bool gameEntered = await client.WaitLogAsync("Entered screen \"Game\"");
        Assert.True(gameEntered, "Failed to enter the Game screen");

        bool intoPlaying = await client.WaitLogAsync("Player into playing.");
        Assert.True(intoPlaying, "Failed into playing state");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestServerReconnectionWithServerExitFirst() {
        // 关闭服务器
        await server.PushCommandAsync("EXEC " + 
            GameTools.ReadScript("GameStartup/DisconnectCode.cs"));

        // 等待客户端日志以确认对话框已显示
        bool disconnectDialogShown = await client.WaitLogAsync("Dialog shown: Disconnected");
        Assert.True(disconnectDialogShown, "Client did not show the Disconnected dialog.");

        await server.PushCommandAsync("EXEC " + 
            GameTools.ReadScript("GameStartup/ServerRestartCode.cs"));
        await client.PushCommandAsync("EXEC " + 
            GameTools.ReadScript("GameStartup/HideAllDialogsCode.cs"));
        await client.PushCommandAsync("EXEC " + 
            GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs")
            .Replace("{0}", server.GamePort.ToString()));
        
        bool clientGameEntered = await client.WaitLogAsync("Entered screen \"Game\"");
        Assert.True(clientGameEntered, "Client failed to enter the Game screen");

        bool clientIntoPlaying = await client.WaitLogAsync("Player into playing.");
        Assert.True(clientIntoPlaying, "Client failed into playing state");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
