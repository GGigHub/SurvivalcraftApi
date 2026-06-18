using System;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class GameStartupTest : IClassFixture<GameProcessFixture> {
    private readonly GameProcessFixture fix;
    private GameProcessContext server => fix.Server;
    private GameProcessContext client => fix.Client;


    public GameStartupTest(GameProcessFixture fixture) {
        this.fix = fixture;
    }

    // 串行按顺序执行以下所有测试方法
    [Fact, TestOrder(Priority = 1)]
    public async Task TestGameEntersSuccessfully() {
        bool mainScreenEntered = await fix.WaitLogAsync("Entered screen \"MainMenu\"",
            server, 5);

        Assert.True(mainScreenEntered,
            "Did not receive the target log message within the timeout period.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestWorldCreation() {
        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playScreenEntered = await fix.WaitLogAsync("Entered screen \"Play\"", server, 10);
        Assert.True(playScreenEntered, "Failed to enter the Play screen");

        await server.PushCommandAsync("CLICK_WIDGET NewWorld");
        bool newWorldScreenEntered = await fix.WaitLogAsync("Entered screen \"NewWorld\"", server, 10);
        Assert.True(newWorldScreenEntered, "Failed to enter the New World screen");

        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playerScreen = await fix.WaitLogAsync("Entered screen \"Player\"", server, 10);
        Assert.True(playerScreen, "Failed to load the Player screen");

        await server.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await fix.WaitLogAsync("Entered screen \"Game\"", server, 10);
        Assert.True(gameEntered, "Failed to enter the Game screen");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestServerConnection() {
        client.Start("--join", "127.0.0.1:28887");

        bool gameLoadingEntered = await fix.WaitLogAsync("Entered screen \"GameLoading\"", client, 10);
        Assert.True(gameLoadingEntered, "Client failed to enter the GameLoading screen.");

        bool connected = await fix.WaitLogAsync("Connected: 127.0.0.1:28887", client, 10);
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await fix.WaitLogAsync("Entered screen \"Player\"", client, 10);
        Assert.True(playerScreen, "Failed to load the Player screen");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await fix.WaitLogAsync("Entered screen \"Game\"", client, 10);
        Assert.True(gameEntered, "Failed to enter the Game screen");

    }
}
