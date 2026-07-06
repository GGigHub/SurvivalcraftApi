using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class DamageSyncTest : IClassFixture<GameFixture> {



    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public DamageSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestWorldCreationAndConnection() {
        await GameTools.CreateTestWorld(server, "damage-sync");
        await DamageSyncTools.ConnectTestServerAsync(client, server.GamePort);
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestComponentDamageHitpointsSyncsToClient() {
        int boatId = await DamageSyncTools.CreateBoatAsync(server, 8f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageSyncTools.DamageEntityState serverState = await DamageSyncTools.DamageBoatAsync(server, boatId, 0.25f);
        Assert.True(serverState.Exists, $"Server did not produce a damage state. State={serverState}.");
        Assert.InRange(serverState.Hitpoints, 0.75f - DamageSyncTools.HitpointsEpsilon, 0.75f + DamageSyncTools.HitpointsEpsilon);

        DamageSyncTools.DamageEntityState clientState = await DamageSyncTools.WaitForDamageEntityStateAsync(client, boatId, serverState.Hitpoints);

        Assert.True(DamageSyncTools.DamageStatesMatch(serverState, clientState), $"ComponentDamage state mismatch. Server={serverState}, Client={clientState}.");
    }

    // 额外测试Density
    [Fact, TestOrder(Priority = 3)]
    public async Task TestBoatDamageThresholdSyncsClientDensity() {
        int boatId = await DamageSyncTools.CreateBoatAsync(server, 10f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageSyncTools.DamageEntityState serverState = await DamageSyncTools.DamageBoatAsync(server, boatId, 0.5f);
        Assert.True(serverState.Exists, $"Server did not produce a damage state. State={serverState}.");
        Assert.InRange(serverState.Hitpoints, 0.5f - DamageSyncTools.HitpointsEpsilon, 0.5f + DamageSyncTools.HitpointsEpsilon);

        DamageSyncTools.DamageEntityState clientState = await DamageSyncTools.WaitForBoatDensityAsync(client, boatId, 0.7f);

        Assert.InRange(clientState.Hitpoints, 0.5f - DamageSyncTools.HitpointsEpsilon, 0.5f + DamageSyncTools.HitpointsEpsilon);
        Assert.InRange(clientState.Density, 0.7f - DamageSyncTools.DensityEpsilon, 0.7f + DamageSyncTools.DensityEpsilon);
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestDestroyedDamageEntityIsRemovedOnClient() {
        int boatId = await DamageSyncTools.CreateBoatAsync(server, 12f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageSyncTools.DamageEntityState serverState = await DamageSyncTools.DamageBoatAsync(server, boatId, 1f);
        Assert.True(serverState.Exists && serverState.Hitpoints <= DamageSyncTools.HitpointsEpsilon, $"Server boat was not destroyed. State={serverState}.");

        Assert.True(await DamageSyncTools.WaitForEntityMissingAsync(client, boatId), $"Client still has destroyed boat entity {boatId}.");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestClientLocalComponentDamageDoesNotChangeServerHitpoints() {
        int boatId = await DamageSyncTools.CreateBoatAsync(server, 14f, 1f, 8f);
        Assert.True(boatId > 0, "Server did not create a boat entity.");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(client, boatId), $"Client did not receive boat entity {boatId}.");

        DamageSyncTools.DamageEntityState initialServerState = await DamageSyncTools.GetDamageEntityStateAsync(server, boatId);
        DamageSyncTools.DamageEntityState clientAttemptState = await DamageSyncTools.DamageBoatAsync(client, boatId, 0.25f);
        await Task.Delay(500);
        DamageSyncTools.DamageEntityState finalServerState = await DamageSyncTools.GetDamageEntityStateAsync(server, boatId);

        Assert.True(clientAttemptState.Exists, $"Client boat was missing during local damage attempt. State={clientAttemptState}.");
        Assert.InRange(finalServerState.Hitpoints, initialServerState.Hitpoints - DamageSyncTools.HitpointsEpsilon, initialServerState.Hitpoints + DamageSyncTools.HitpointsEpsilon);
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task TestActiveToolDamageSyncsToClientInventory() {
        int playerIndex = await DamageSyncTools.WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await DamageSyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await DamageSyncTools.WaitForPlayerInventoryAsync(client, playerIndex), $"Client player inventory was not available for player {playerIndex}.");

        DamageSyncTools.ToolSlotState serverState = await DamageSyncTools.GiveAndDamageActiveToolAsync(server, playerIndex, 5);
        Assert.True(serverState.Exists, $"Server did not produce a damaged tool state. State={serverState}.");
        Assert.True(serverState.Count == 1, $"Server tool count mismatch. State={serverState}.");
        Assert.True(serverState.Damage == 5, $"Server tool damage mismatch. State={serverState}.");

        DamageSyncTools.ToolSlotState clientState = await DamageSyncTools.WaitForToolSlotStateAsync(client, playerIndex, serverState);

        Assert.Equal(serverState.Value, clientState.Value);
        Assert.Equal(serverState.Count, clientState.Count);
        Assert.Equal(serverState.Contents, clientState.Contents);
        Assert.Equal(serverState.Damage, clientState.Damage);
    }

    [Fact, TestOrder(Priority = 7)]
    public async Task TestActiveToolBreakSyncsEmptySlotToClientInventory() {
        int playerIndex = await DamageSyncTools.WaitForClientPlayerIndexAsync(client);
        int serverPlayerEntityId = await DamageSyncTools.EnsureServerPlayerEntityAsync(server, playerIndex);
        Assert.True(serverPlayerEntityId > 0, $"Server player entity was not available for player {playerIndex}.");
        Assert.True(await DamageSyncTools.WaitForPlayerInventoryAsync(client, playerIndex), $"Client player inventory was not available for player {playerIndex}.");

        DamageSyncTools.ToolSlotState serverState = await DamageSyncTools.GiveAndBreakActiveToolAsync(server, playerIndex);
        Assert.True(serverState.Exists, $"Server did not produce a broken tool state. State={serverState}.");
        Assert.True(serverState.Value == 0, $"Server broken tool value mismatch. State={serverState}.");
        Assert.True(serverState.Count == 0, $"Server broken tool count mismatch. State={serverState}.");

        DamageSyncTools.ToolSlotState clientState = await DamageSyncTools.WaitForToolSlotStateAsync(client, playerIndex, serverState);

        Assert.Equal(0, clientState.Value);
        Assert.Equal(0, clientState.Count);
        Assert.Equal(serverState.Damage, clientState.Damage);
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
