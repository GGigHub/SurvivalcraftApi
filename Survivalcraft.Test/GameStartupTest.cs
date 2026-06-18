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
    [Fact, TestOrder(Priority = 1)]
    public async Task TestGameEntersSuccessfully() {
        bool mainScreenEntered = await server.WaitLogAsync(
            "Entered screen \"MainMenu\"", 5);

        Assert.True(mainScreenEntered,
            "Did not receive the target log message within the timeout period.");
    }

    [Fact, TestOrder(Priority = 2)]
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

    [Fact, TestOrder(Priority = 3)]
    public async Task TestServerConnection() {
        client.Start("--join", "127.0.0.1:28887");

        bool gameLoadingEntered = await client.WaitLogAsync(
            "Entered screen \"GameLoading\"", 10);
        Assert.True(gameLoadingEntered, "Client failed to enter the GameLoading screen.");

        bool connected = await client.WaitLogAsync("Connected: 127.0.0.1:28887", 10);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen");
    }
}
