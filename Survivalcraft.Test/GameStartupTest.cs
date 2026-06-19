using System;
using System.Threading;
using System.Threading.Tasks;
using Game;
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

    // 串行按顺序执行以下所有测试方法
    // 运行测试时请先编译测试模组
    // 右键测试项目->运行测试
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
        client.Start("--join", "127.0.0.1:28887");

        bool gameLoadingEntered = await fixture.WaitLogAsync("Entered screen \"GameLoading\"", client);
        Assert.True(gameLoadingEntered, "Client failed to enter the GameLoading screen.");

        bool connected = await fixture.WaitLogAsync("Connected: 127.0.0.1:28887", client);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await fixture.WaitLogAsync("Entered screen \"Player\"", client);
        Assert.True(playerScreen, "Failed to load the Player screen");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await fixture.WaitLogAsync("Entered screen \"Game\"", client);
        Assert.True(gameEntered, "Failed to enter the Game screen");
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
            "\"GameLoading\", null, null, System.Net.IPEndPoint.Parse(\"127.0.0.1:28887\"), null);");

        bool gameLoadingEntered = await fixture.WaitLogAsync("Entered screen \"GameLoading\"", client);
        Assert.True(gameLoadingEntered, "Client failed to enter the GameLoading screen during reconnection.");

        bool connected = await fixture.WaitLogAsync("Connected: 127.0.0.1:28887", client);
        Assert.True(connected, "Client failed to reconnect to the server.");

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        var (success, context) = await fixture.WaitLogAsync(client, "Entered screen",  timeout.Token);
        Assert.True(context.Contains("Game"), "Player data was not loaded correctly.");
        Assert.False(context.Contains("Player"), "Player screen should not be loaded, but Game screen should be loaded.");
    }
}
