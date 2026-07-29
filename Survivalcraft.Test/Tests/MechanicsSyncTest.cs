using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class MechanicsSyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public MechanicsSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestWorldCreationAndConnection() {
        await GameTools.CreateTestWorld(server, "mechanics-sync");
        await MechanicsSyncTools.ConnectTestServerAsync(client, server.GamePort);
        Assert.True(await MechanicsSyncTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestSleepAndWakeupSync() {
        int playerIndex = await MechanicsSyncTools.WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await MechanicsSyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");

        MechanicsSyncTools.SleepState prepared = await MechanicsSyncTools.PrepareSleepAsync(server, playerIndex);
        Assert.True(prepared.Exists, $"Server player sleep component was not ready. State={prepared.Raw}");

        MechanicsSyncTools.SleepState requested = await MechanicsSyncTools.StartSleepAsync(client, playerIndex);
        Assert.True(requested.Exists, $"Client did not send sleep request. State={requested.Raw}");

        MechanicsSyncTools.SleepState serverSleeping = await MechanicsSyncTools.WaitForSleepStateAsync(server, playerIndex, true);
        MechanicsSyncTools.SleepState clientSleeping = await MechanicsSyncTools.WaitForSleepStateAsync(client, playerIndex, true);
        Assert.True(serverSleeping.IsSleeping && clientSleeping.IsSleeping, $"Sleep state did not synchronize. Server={serverSleeping.Raw}, Client={clientSleeping.Raw}");
        Assert.Equal(serverSleeping.AllowManualWakeup, clientSleeping.AllowManualWakeup);

        (MechanicsSyncTools.SleepState serverAwake, MechanicsSyncTools.SleepState clientAwake) =
            await MechanicsSyncTools.WakeUpAndWaitForAwakeAsync(client, server, playerIndex);
        Assert.False(serverAwake.IsSleeping);
        Assert.False(clientAwake.IsSleeping);
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestMountAndDismountSync() {
        int playerIndex = await MechanicsSyncTools.WaitForClientPlayerIndexAsync(client);
        // The authoritative mount protocol enforces the production interaction
        // range. Keep this legacy positive-path fixture genuinely reachable;
        // the typed mount scenario separately proves that a far target is rejected.
        int boatId = await MechanicsSyncTools.CreateBoatNearPlayerAsync(server, playerIndex, 1f, 0f, 0f);
        Assert.True(boatId > 0, "Server did not create mountable boat.");
        Assert.True(
            await MechanicsSyncTools.PrepareBoatForMountAsync(server, playerIndex, boatId),
            "Server did not stabilize the legacy mount fixture.");
        Assert.True(await MechanicsSyncTools.WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");
        MechanicsSyncTools.MountProtocolState protocolState =
            await MechanicsSyncTools.WaitForMountProtocolReadyAsync(client, playerIndex, boatId);
        Assert.True(protocolState.IsReady, $"Mount protocol baselines were incomplete. State={protocolState.Raw}");

        MechanicsSyncTools.MountState requested = await MechanicsSyncTools.StartMountingAsync(client, playerIndex, boatId);
        Assert.True(requested.Exists, $"Client did not request mounting. State={requested.Raw}");

        MechanicsSyncTools.MountState serverMounted = await MechanicsSyncTools.WaitForMountStateAsync(server, playerIndex, boatId);
        MechanicsSyncTools.MountState clientMounted = await MechanicsSyncTools.WaitForMountStateAsync(client, playerIndex, boatId);
        Assert.Equal(boatId, serverMounted.MountId);
        Assert.Equal(boatId, clientMounted.MountId);

        MechanicsSyncTools.MountState dismountRequested = await MechanicsSyncTools.StartDismountingAsync(client, playerIndex);
        Assert.True(dismountRequested.Exists, $"Client did not request dismounting. State={dismountRequested.Raw}");

        MechanicsSyncTools.MountState serverDismounted = await MechanicsSyncTools.WaitForMountStateAsync(server, playerIndex, 0);
        MechanicsSyncTools.MountState clientDismounted = await MechanicsSyncTools.WaitForMountStateAsync(client, playerIndex, 0);
        Assert.Equal(0, serverDismounted.MountId);
        Assert.Equal(0, clientDismounted.MountId);
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestEntityFireAndFireBlockSync() {
        int playerIndex = await MechanicsSyncTools.WaitForClientPlayerIndexAsync(client);
        MechanicsSyncTools.EntityFireState serverOnFire = await MechanicsSyncTools.SetPlayerOnFireAsync(server, playerIndex, 12f);
        Assert.True(serverOnFire.IsOnFire, $"Server player did not catch fire. State={serverOnFire.Raw}");

        MechanicsSyncTools.EntityFireState clientOnFire = await MechanicsSyncTools.WaitForEntityFireStateAsync(client, playerIndex, true);
        Assert.True(clientOnFire.Duration > 0f, $"Client did not receive entity fire duration. State={clientOnFire.Raw}");

        MechanicsSyncTools.FireBlockState added = await MechanicsSyncTools.AddFireBlockAsync(server, playerIndex);
        Assert.True(added.OnFire, $"Server did not add fire block. State={added.Raw}");

        MechanicsSyncTools.FireBlockState clientAdded = await MechanicsSyncTools.WaitForFireBlockStateAsync(client, added, true);
        Assert.True(clientAdded.OnFire, $"Client did not receive fire block add. Server={added.Raw}, Client={clientAdded.Raw}");

        MechanicsSyncTools.FireBlockState removed = await MechanicsSyncTools.RemoveFireBlockAsync(server, added);
        Assert.False(removed.OnFire, $"Server did not remove fire block. State={removed.Raw}");

        MechanicsSyncTools.FireBlockState clientRemoved = await MechanicsSyncTools.WaitForFireBlockStateAsync(client, added, false);
        Assert.False(clientRemoved.OnFire, $"Client did not receive fire block remove. Server={removed.Raw}, Client={clientRemoved.Raw}");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestSicknessAndNauseaSync() {
        int playerIndex = await MechanicsSyncTools.WaitForClientPlayerIndexAsync(client);
        MechanicsSyncTools.SicknessState serverSick = await MechanicsSyncTools.StartSicknessAsync(server, playerIndex);
        Assert.True(serverSick.IsSick, $"Server did not start sickness. State={serverSick.Raw}");

        MechanicsSyncTools.SicknessState clientSick = await MechanicsSyncTools.WaitForSicknessAsync(client, playerIndex, true);
        Assert.True(clientSick.Duration > 0f, $"Client did not receive sickness duration. State={clientSick.Raw}");

        MechanicsSyncTools.SicknessState serverNausea = await MechanicsSyncTools.TriggerNauseaAsync(server, playerIndex);
        Assert.True(serverNausea.HasNausea, $"Server nausea effect did not run. State={serverNausea.Raw}");

        MechanicsSyncTools.SicknessState clientNausea = await MechanicsSyncTools.WaitForNauseaAsync(client, playerIndex);
        Assert.True(clientNausea.HasNausea, $"Client did not receive nausea effect. Server={serverNausea.Raw}, Client={clientNausea.Raw}");
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task TestProjectileCrossbowExplosionAndHitValueSync() {
        int playerIndex = await MechanicsSyncTools.WaitForClientPlayerIndexAsync(client);

        MechanicsSyncTools.ProjectileState serverProjectile = await MechanicsSyncTools.AddProjectileAsync(server, playerIndex);
        Assert.True(serverProjectile.Count > 0, $"Server did not add projectile. State={serverProjectile.Raw}");
        MechanicsSyncTools.ProjectileState clientProjectile = await MechanicsSyncTools.WaitForProjectileAsync(client, serverProjectile.Value);
        Assert.True(clientProjectile.Count > 0, $"Client did not receive projectile. Server={serverProjectile.Raw}, Client={clientProjectile.Raw}");

        MechanicsSyncTools.CrossbowState serverInitial = await MechanicsSyncTools.GiveCrossbowAsync(server, playerIndex);
        Assert.Equal(0, serverInitial.Draw);
        MechanicsSyncTools.CrossbowState clientInitial = await MechanicsSyncTools.WaitForCrossbowDrawAsync(client, playerIndex, 0);
        Assert.Equal(0, clientInitial.Draw);

        MechanicsSyncTools.CrossbowState clientPulled = await MechanicsSyncTools.PullCrossbowAsync(client, playerIndex);
        Assert.Equal(15, clientPulled.Draw);
        MechanicsSyncTools.CrossbowState serverPulled = await MechanicsSyncTools.WaitForCrossbowDrawAsync(server, playerIndex, 15);
        Assert.Equal(15, serverPulled.Draw);

        MechanicsSyncTools.ExplosionState serverExplosion = await MechanicsSyncTools.TriggerExplosionAsync(server, playerIndex);
        Assert.True(serverExplosion.Cells > 0, $"Server explosion did not create particle cells. State={serverExplosion.Raw}");
        MechanicsSyncTools.ExplosionState clientExplosion = await MechanicsSyncTools.WaitForExplosionAsync(client, serverExplosion);
        Assert.True(clientExplosion.Cells > 0, $"Client did not receive explosion cells. Server={serverExplosion.Raw}, Client={clientExplosion.Raw}");

        int boatId = await MechanicsSyncTools.CreateBoatNearPlayerAsync(server, playerIndex, 6f, 1f, 6f);
        Assert.True(await MechanicsSyncTools.WaitForEntityExistsAsync(client, boatId), $"Client did not receive hit-value target boat {boatId}.");
        MechanicsSyncTools.HitValueState serverHitValue = await MechanicsSyncTools.AttackForHitValueAsync(server, boatId);
        Assert.True(serverHitValue.Exists, $"Server attack did not create hit-value particle. State={serverHitValue.Raw}");
        MechanicsSyncTools.HitValueState clientHitValue = await MechanicsSyncTools.WaitForHitValueAsync(client, serverHitValue.Text);
        Assert.True(clientHitValue.Exists, $"Client did not receive hit-value particle. Server={serverHitValue.Raw}, Client={clientHitValue.Raw}");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
