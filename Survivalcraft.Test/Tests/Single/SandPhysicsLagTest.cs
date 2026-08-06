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

    [Fact, TestOrder(Priority = 4)]
    public async Task TestClientDigSandLeavesNoGhostSand() {
        // DiamondShovelBlock index, no data/light bits.
        const int diamondShovelValue = 113;

        if (await server.PushCommandAsync("FUNC return Game.GameManager.Project != null;") is not true) {
            await GameTools.CreateTestWorld(server, "sand-physics-clientdig");
        }

        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        if (playerIndex < 0) {
            try {
                playerIndex = await PlayerSyncTools.ConnectAndCreatePlayerAsync(client, server.GamePort, "TestClientPlayer");
            }
            catch {
                output.WriteLine("SERVER LOGS:\n" + server.GetRecentLogText(300));
                output.WriteLine("CLIENT LOGS:\n" + client.GetRecentLogText(300));
                throw;
            }
        }
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
        Assert.True(await SandPhysicsTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");

        SandPhysicsTools.SandTarget target = await SandPhysicsTools.SetupClientSandColumnAsync(server, playerIndex);
        output.WriteLine($"Target: {target.Raw}");

        SandPhysicsTools.SandPhysicsState synced = await SandPhysicsTools.WaitForSandVisibleAsync(client, target);
        Assert.True(
            synced.OrigContents == synced.SandIndex,
            $"Client terrain did not sync the sand column before digging. State={synced.Raw}");

        // The bottom sand block is the dig target; the client player is moved
        // next to it with a diamond shovel for a fast dig.
        InteractionTarget digTarget = new(
            target.X,
            target.Y - 1,
            target.Z,
            target.X,
            target.Y - 1,
            target.Z,
            0,
            0,
            0,
            0,
            diamondShovelValue,
            1,
            0,
            string.Empty);

        await PlayerSyncTools.PrepareClientPlayerForActionAsync(client, digTarget);
        await PlayerSyncTools.InjectDigInputAsync(client, digTarget);

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
            serverState.OrigContents == 0,
            $"Server sand did not fall from origin. State={serverState.Raw}");

        Assert.True(
            clientState.OrigContents == 0,
            $"BUG REPRODUCED (Ghost Sand): Client still shows sand at the origin while the server has air. State={clientState.Raw}");

        Assert.True(
            serverState.CollapsingSets == 0,
            $"BUG REPRODUCED (Server): Server retained {serverState.CollapsingSets} lingering MovingBlockSet(s). State={serverState.Raw}");

        Assert.True(
            clientState.CollapsingSets == 0,
            $"BUG REPRODUCED (Client): Client retained {clientState.CollapsingSets} lingering MovingBlockSet(s). State={clientState.Raw}");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
