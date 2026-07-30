using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class SandPhysicsLagTest : IClassFixture<GameFixture> {
    private readonly ITestOutputHelper output;
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public SandPhysicsLagTest(GameFixture fixture, ITestOutputHelper output) {
        this.fixture = fixture;
        this.output = output;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestSingleplayerSandFallCleanup() {
        await GameTools.CreateTestWorld(server, "sand-physics-singleplayer");

        SandPhysicsTools.SandTarget target = await SandPhysicsTools.SetupSandColumnAsync(server);
        output.WriteLine($"Target: {target.Raw}");

        SandPhysicsTools.SandPhysicsState initial = await SandPhysicsTools.GetSandPhysicsStateAsync(server, target);
        output.WriteLine($"Initial State: {initial.Raw}");

        await SandPhysicsTools.DigSupportBlockAsync(server, target);

        SandPhysicsTools.SandPhysicsState finalState = await SandPhysicsTools.WaitForSandLandingAsync(server, target);
        output.WriteLine($"Final State: {finalState.Raw}");

        Assert.True(
            finalState.LandContents == finalState.SandIndex,
            $"Sand did not land. FinalState={finalState.Raw}");

        Assert.True(
            finalState.CollapsingSets == 0,
            $"BUG REPRODUCED: Server SubsystemMovingBlocks retained {finalState.CollapsingSets} lingering MovingBlockSet(s) after sand landed! (Expected 0). FinalState={finalState.Raw}");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestMultiplayerSandFallClientLagAndCleanup() {
        await SandPhysicsTools.ConnectTestServerAsync(client, server.GamePort);
        Assert.True(await SandPhysicsTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");

        SandPhysicsTools.SandTarget target = await SandPhysicsTools.SetupSandColumnAsync(server);
        output.WriteLine($"Target: {target.Raw}");

        await SandPhysicsTools.DigSupportBlockAsync(server, target);

        SandPhysicsTools.SandPhysicsState serverState = await SandPhysicsTools.WaitForSandLandingAsync(server, target);
        SandPhysicsTools.SandPhysicsState clientState = await SandPhysicsTools.WaitForSandLandingAsync(client, target);
        output.WriteLine($"Server State: {serverState.Raw}");
        output.WriteLine($"Client State: {clientState.Raw}");

        Assert.True(
            serverState.LandContents == serverState.SandIndex,
            $"Server sand did not land. Expected SandIndex={serverState.SandIndex}, State={serverState.Raw}");

        Assert.True(
            clientState.LandContents == clientState.SandIndex,
            $"Client sand did not land. Expected SandIndex={clientState.SandIndex}, State={clientState.Raw}");

        Assert.True(
            serverState.CollapsingSets == 0,
            $"BUG REPRODUCED (Server): Server retained {serverState.CollapsingSets} lingering MovingBlockSet(s). State={serverState.Raw}");

        Assert.True(
            clientState.CollapsingSets == 0,
            $"BUG REPRODUCED (Client): Client retained {clientState.CollapsingSets} lingering MovingBlockSet(s) after sand landed! State={clientState.Raw}");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestRepeatedSandDigAccumulationBug() {
        int expectedClientLingeringSets = 0;
        for (int i = 1; i <= 3; i++) {
            SandPhysicsTools.SandTarget target = await SandPhysicsTools.SetupSandColumnAsync(server);
            output.WriteLine($"Iteration {i} Target: {target.Raw}");

            await SandPhysicsTools.DigSupportBlockAsync(server, target);

            SandPhysicsTools.SandPhysicsState serverState = await SandPhysicsTools.WaitForSandLandingAsync(server, target);
            SandPhysicsTools.SandPhysicsState clientState = await SandPhysicsTools.WaitForSandLandingAsync(client, target);
            output.WriteLine($"Iter {i} Server: {serverState.Raw}");
            output.WriteLine($"Iter {i} Client: {clientState.Raw}");

            Assert.True(
                clientState.CollapsingSets == expectedClientLingeringSets,
                $"BUG REPRODUCED (Accumulation Iteration {i}): Client moving block sets accumulated to {clientState.CollapsingSets} lingering sets after {i} sand dig(s)! ServerState={serverState.Raw}, ClientState={clientState.Raw}");
        }
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
