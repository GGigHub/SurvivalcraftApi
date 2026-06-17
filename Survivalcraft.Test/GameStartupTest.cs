using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class GameStartupTest : IClassFixture<GameProcessFixture> {
    private readonly GameProcessFixture fixture;

    public GameStartupTest(GameProcessFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestGameEntersSuccessfully() {
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

        bool foundLog = await fixture.WaitLogAsync(
            "Entered screen \"MainMenu\"", cts.Token);

        Assert.True(foundLog,
            "Did not receive the target log message within the timeout period.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestWorldCreation() {
        await fixture.SendCommandAsync("CLICK_WIDGET Play");
        bool playScreenEntered = await fixture.WaitLogAsync("Entered screen \"Play\"");
        Assert.True(playScreenEntered, "Failed to enter the Play screen");

        await fixture.SendCommandAsync("CLICK_WIDGET NewWorld");
        bool newWorldScreenEntered = await fixture.WaitLogAsync("Entered screen \"NewWorld\"");
        Assert.True(newWorldScreenEntered, "Failed to enter the New World screen");

        await fixture.SendCommandAsync("CLICK_WIDGET Play");
        bool worldLoaded = await fixture.WaitLogAsync("Entered screen \"GameLoading\"");
        Assert.True(worldLoaded, "Failed to load the new world");

        bool gameEntered = await fixture.WaitLogAsync("Entered screen \"Game\"", 3);
        Assert.True(gameEntered, "Failed to enter the Game screen");
    } 
}
