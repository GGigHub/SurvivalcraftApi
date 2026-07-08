using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class PlayerSyncTest : IClassFixture<GameFixture> {
    private const string CreatedPlayerName = "SyncCreateClient";
    private const string ModifiedPlayerName = "SyncModifiedClient";

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;
    private GameContext spectator => fixture.Spectator;

    public PlayerSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestPlayerCreateIsVisibleToObserverClient() {
        await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "player-sync");
        await PlayerSyncTools.ConnectObserverAsync(spectator, server.GamePort);

        int clientPlayerIndex = await PlayerSyncTools.ConnectAndCreatePlayerAsync(client, server.GamePort, CreatedPlayerName);

        PlayerState serverPlayer = await PlayerSyncTools.WaitForPlayerAsync(server, clientPlayerIndex, CreatedPlayerName);
        PlayerState clientPlayer = await PlayerSyncTools.WaitForPlayerAsync(client, clientPlayerIndex, CreatedPlayerName);
        PlayerState spectatorPlayer = await PlayerSyncTools.WaitForPlayerAsync(spectator, clientPlayerIndex, CreatedPlayerName);

        Assert.True(serverPlayer.Exists, $"Server did not contain created player {clientPlayerIndex}.");
        Assert.True(clientPlayer.Exists, $"Client did not contain created player {clientPlayerIndex}.");
        Assert.True(spectatorPlayer.Exists, $"Spectator did not contain created player {clientPlayerIndex}.");
        Assert.Equal(serverPlayer.Name, spectatorPlayer.Name);
        Assert.Equal(serverPlayer.SkinName, spectatorPlayer.SkinName);
        Assert.Equal(serverPlayer.PlayerClass, spectatorPlayer.PlayerClass);
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestPlayerModifyIsVisibleToObserverClient() {
        PlayerState modifiedClientPlayer = await PlayerSyncTools.ModifyMainPlayerAsync(client, ModifiedPlayerName);

        PlayerState serverPlayer = await PlayerSyncTools.WaitForPlayerAsync(server, modifiedClientPlayer.Index, ModifiedPlayerName);
        PlayerState spectatorPlayer = await PlayerSyncTools.WaitForPlayerAsync(spectator, modifiedClientPlayer.Index, ModifiedPlayerName);

        Assert.True(serverPlayer.Exists, $"Server did not receive modified player {modifiedClientPlayer.Index}. Client={modifiedClientPlayer}.");
        Assert.True(spectatorPlayer.Exists, $"Spectator did not receive modified player {modifiedClientPlayer.Index}. Client={modifiedClientPlayer}.");
        Assert.Equal(modifiedClientPlayer.Name, serverPlayer.Name);
        Assert.Equal(modifiedClientPlayer.Name, spectatorPlayer.Name);
        Assert.Equal(modifiedClientPlayer.SkinName, serverPlayer.SkinName);
        Assert.Equal(modifiedClientPlayer.SkinName, spectatorPlayer.SkinName);
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestMainPlayerBodyStateSyncsToLateJoiningObserverClient() {
        PlayerState clientPlayer = await PlayerSyncTools.WaitForMainPlayerEntityAsync(client);
        Assert.True(clientPlayer.Exists && clientPlayer.Index >= 0, $"Client main player entity was not ready. Player={clientPlayer}.");

        BodySyncState clientState = await CreatureSyncTools.OffsetMainPlayerBodyPositionAsync(
            client,
            new Vector3Dto(1.8f, 0f, 0.85f),
            new Vector3Dto(0f, 0f, 0f));
        Assert.True(clientState.Exists, $"Client main player body was not available. State={clientState}.");

        BodySyncState serverState = await CreatureSyncTools.WaitForPlayerNearAsync(server, clientPlayer.Index, clientState);

        await PlayerSyncTools.EnterObserverPlayingAsync(spectator);
        BodySyncState spectatorState = await CreatureSyncTools.WaitForPlayerNearAsync(spectator, clientPlayer.Index, clientState);

        Assert.True(
            CreatureSyncTools.PlayerBodyStatesNear(clientState, serverState),
            $"Late-join body setup did not synchronize to server. Client={clientState}, Server={serverState}.");
        Assert.True(
            CreatureSyncTools.PlayerBodyStatesNear(clientState, spectatorState),
            $"Late-joining observer did not receive current player body state. Client={clientState}, Observer={spectatorState}.");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestMainPlayerBodyUpdateIsVisibleToObserverClient() {
        PlayerState clientPlayer = await PlayerSyncTools.WaitForMainPlayerEntityAsync(client);
        Assert.True(clientPlayer.Exists && clientPlayer.Index >= 0, $"Client main player entity was not ready. Player={clientPlayer}.");

        await PlayerSyncTools.EnterObserverPlayingAsync(spectator);
        await CreatureSyncTools.WaitForPlayerBodyAsync(server, clientPlayer.Index);
        await CreatureSyncTools.WaitForPlayerBodyAsync(spectator, clientPlayer.Index);

        BodySyncState clientState = await CreatureSyncTools.OffsetMainPlayerBodyPositionAsync(
            client,
            new Vector3Dto(1.15f, 0f, -0.4f),
            new Vector3Dto(0f, 0f, 0f));
        Assert.True(clientState.Exists, $"Client main player body was not available. State={clientState}.");

        BodySyncState serverState = await CreatureSyncTools.WaitForPlayerNearAsync(server, clientPlayer.Index, clientState);
        BodySyncState spectatorState = await CreatureSyncTools.WaitForPlayerNearAsync(spectator, clientPlayer.Index, clientState);

        Assert.True(
            CreatureSyncTools.PlayerBodyStatesNear(clientState, serverState),
            $"Main player body update did not synchronize to server. Client={clientState}, Server={serverState}.");
        Assert.True(
            CreatureSyncTools.PlayerBodyStatesNear(clientState, spectatorState),
            $"Main player body update did not broadcast to observer. Client={clientState}, Observer={spectatorState}.");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestMainPlayerLookAndCrouchStateIsVisibleToObserverClient() {
        PlayerState clientPlayer = await PlayerSyncTools.WaitForMainPlayerEntityAsync(client);
        Assert.True(clientPlayer.Exists && clientPlayer.Index >= 0, $"Client main player entity was not ready. Player={clientPlayer}.");

        await PlayerSyncTools.EnterObserverPlayingAsync(spectator);
        await CreatureSyncTools.WaitForPlayerBodyAsync(server, clientPlayer.Index);
        await CreatureSyncTools.WaitForPlayerBodyAsync(spectator, clientPlayer.Index);

        PlayerBodyStatusState clientState = await PlayerSyncTools.SetMainPlayerBodyStatusAsync(
            client,
            new Vector2Dto(0.41f, -0.27f),
            isCrouching: true);
        Assert.True(clientState.Exists, $"Client main player body status was not available. State={clientState}.");
        Assert.True(clientState.IsCrouching, $"Client did not enter crouch state. State={clientState}.");

        PlayerBodyStatusState serverState = await PlayerSyncTools.WaitForPlayerBodyStatusNearAsync(server, clientPlayer.Index, clientState);
        PlayerBodyStatusState spectatorState = await PlayerSyncTools.WaitForPlayerBodyStatusNearAsync(spectator, clientPlayer.Index, clientState);

        Assert.True(
            PlayerSyncTools.PlayerBodyStatusNear(clientState, serverState),
            $"Main player look/crouch state did not synchronize to server. Client={clientState}, Server={serverState}.");
        Assert.True(
            PlayerSyncTools.PlayerBodyStatusNear(clientState, spectatorState),
            $"Main player look/crouch state did not broadcast to observer. Client={clientState}, Observer={spectatorState}.");
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task TestCreativeFlyToggleBroadcastsToObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");

        await client.PushCommandAsync("CLICK_WIDGET CreativeFlyButton");

        PlayerRuntimeState clientEnabled = await PlayerSyncTools.WaitForCreativeFlyAsync(client, playerIndex, expected: true);
        PlayerRuntimeState serverEnabled = await PlayerSyncTools.WaitForCreativeFlyAsync(server, playerIndex, expected: true);
        PlayerRuntimeState spectatorEnabled = await PlayerSyncTools.WaitForCreativeFlyAsync(spectator, playerIndex, expected: true);

        Assert.True(clientEnabled.IsCreativeFlyEnabled, $"Client did not enable creative fly. State={clientEnabled}.");
        Assert.True(serverEnabled.IsCreativeFlyEnabled, $"Server did not receive creative fly state. State={serverEnabled}.");
        Assert.True(spectatorEnabled.IsCreativeFlyEnabled, $"Spectator did not receive creative fly state. State={spectatorEnabled}.");

        await client.PushCommandAsync("CLICK_WIDGET CreativeFlyButton");

        PlayerRuntimeState clientDisabled = await PlayerSyncTools.WaitForCreativeFlyAsync(client, playerIndex, expected: false);
        PlayerRuntimeState serverDisabled = await PlayerSyncTools.WaitForCreativeFlyAsync(server, playerIndex, expected: false);
        PlayerRuntimeState spectatorDisabled = await PlayerSyncTools.WaitForCreativeFlyAsync(spectator, playerIndex, expected: false);

        Assert.False(clientDisabled.IsCreativeFlyEnabled, $"Client did not disable creative fly. State={clientDisabled}.");
        Assert.False(serverDisabled.IsCreativeFlyEnabled, $"Server did not receive disabled creative fly state. State={serverDisabled}.");
        Assert.False(spectatorDisabled.IsCreativeFlyEnabled, $"Spectator did not receive disabled creative fly state. State={spectatorDisabled}.");
    }

    [Fact, TestOrder(Priority = 7)]
    public async Task TestServerExperienceGainBroadcastsToObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");

        PlayerRuntimeState initialServer = await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);
        Assert.True(initialServer.Exists, $"Server player state was not available. State={initialServer}.");

        PlayerRuntimeState serverAfterGain = await PlayerSyncTools.AddExperienceAsync(server, playerIndex, count: 20);
        Assert.True(
            serverAfterGain.Level > initialServer.Level,
            $"Server level did not increase after experience gain. Before={initialServer}, After={serverAfterGain}.");

        PlayerRuntimeState clientAfterGain = await PlayerSyncTools.WaitForLevelAsync(client, playerIndex, serverAfterGain.Level);
        PlayerRuntimeState spectatorAfterGain = await PlayerSyncTools.WaitForLevelAsync(spectator, playerIndex, serverAfterGain.Level);

        Assert.True(
            Math.Abs(clientAfterGain.Level - serverAfterGain.Level) <= PlayerSyncTools.LevelEpsilon,
            $"Client level did not sync. Server={serverAfterGain}, Client={clientAfterGain}.");
        Assert.True(
            Math.Abs(spectatorAfterGain.Level - serverAfterGain.Level) <= PlayerSyncTools.LevelEpsilon,
            $"Spectator level did not sync. Server={serverAfterGain}, Spectator={spectatorAfterGain}.");
    }

    [Fact, TestOrder(Priority = 8)]
    public async Task TestClientRespawnRecreatesPlayerForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");

        PlayerRuntimeState oldServerState = await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);
        PlayerRuntimeState oldClientState = await PlayerSyncTools.WaitForPlayerEntityAsync(client, playerIndex);
        PlayerRuntimeState oldSpectatorState = await PlayerSyncTools.WaitForPlayerEntityAsync(spectator, playerIndex);
        Assert.True(oldServerState.EntityId > 0, $"Server player entity was not available. State={oldServerState}.");
        Assert.True(oldClientState.EntityId > 0, $"Client player entity was not available. State={oldClientState}.");
        Assert.True(oldSpectatorState.EntityId > 0, $"Spectator player entity was not available. State={oldSpectatorState}.");

        PlayerRuntimeState killedServerState = await PlayerSyncTools.KillPlayerAsync(server, playerIndex);
        Assert.True(killedServerState.Health <= PlayerSyncTools.HealthEpsilon, $"Server did not kill player. State={killedServerState}.");

        PlayerRuntimeState clientDead = await PlayerSyncTools.WaitForDeadAsync(client, playerIndex);
        PlayerRuntimeState spectatorDead = await PlayerSyncTools.WaitForDeadAsync(spectator, playerIndex);
        Assert.True(clientDead.Health <= PlayerSyncTools.HealthEpsilon, $"Client did not receive player death. State={clientDead}.");
        Assert.True(spectatorDead.Health <= PlayerSyncTools.HealthEpsilon, $"Spectator did not receive player death. State={spectatorDead}.");

        string requestResult = await PlayerSyncTools.RequestRespawnAsync(client);
        Assert.DoesNotContain("missing", requestResult, StringComparison.OrdinalIgnoreCase);

        PlayerRuntimeState newServerState = await PlayerSyncTools.WaitForRespawnedAsync(server, playerIndex, oldServerState.EntityId);
        PlayerRuntimeState newClientState = await PlayerSyncTools.WaitForRespawnedAsync(client, playerIndex, oldClientState.EntityId);
        PlayerRuntimeState newSpectatorState = await PlayerSyncTools.WaitForRespawnedAsync(spectator, playerIndex, oldSpectatorState.EntityId);

        Assert.True(newServerState.EntityId != oldServerState.EntityId, $"Server did not recreate player entity. Old={oldServerState}, New={newServerState}.");
        Assert.True(newClientState.EntityId != oldClientState.EntityId, $"Client did not receive recreated player entity. Old={oldClientState}, New={newClientState}.");
        Assert.True(newSpectatorState.EntityId != oldSpectatorState.EntityId, $"Spectator did not receive recreated player entity. Old={oldSpectatorState}, New={newSpectatorState}.");
        Assert.True(newServerState.Health > 0f && newClientState.Health > 0f && newSpectatorState.Health > 0f,
            $"Respawned health was invalid. Server={newServerState}, Client={newClientState}, Spectator={newSpectatorState}.");
    }

    // 不是上船，是放船
    [Fact, TestOrder(Priority = 9)]
    public async Task TestClientUsePathCreatesBoatForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PrepareUseBoatTargetAsync(server, playerIndex);
        Assert.Equal(PlayerSyncTools.BoatBlockIndex, target.ExpectedContents);

        int initialServerCount = await PlayerSyncTools.GetBoatCountNearAsync(server, target);
        int initialSpectatorCount = await PlayerSyncTools.GetBoatCountNearAsync(spectator, target);

        await PlayerSyncTools.InjectInteractInputAsync(client, target);

        int finalServerCount = await PlayerSyncTools.WaitForBoatCountAtLeastAsync(server, target, initialServerCount + 1);
        int finalSpectatorCount = await PlayerSyncTools.WaitForBoatCountAtLeastAsync(spectator, target, initialSpectatorCount + 1);

        Assert.True(finalServerCount >= initialServerCount + 1, $"Server did not create a boat. Target={target.Raw}");
        Assert.True(finalSpectatorCount >= initialSpectatorCount + 1, $"Spectator did not receive the boat entity. Target={target.Raw}");
    }

    [Fact, TestOrder(Priority = 10)]
    public async Task TestClientInteractPathTogglesDoorForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PrepareDoorTargetAsync(server, playerIndex);
        Assert.Equal(PlayerSyncTools.WoodenDoorBlockIndex, target.Contents);

        await PlayerSyncTools.InjectInteractInputAsync(client, target);

        CellState serverState = await PlayerSyncTools.WaitForCellStateAsync(
            server,
            target,
            PlayerSyncTools.WoodenDoorBlockIndex,
            target.ExpectedData);
        CellState spectatorState = await PlayerSyncTools.WaitForCellStateAsync(
            spectator,
            target,
            PlayerSyncTools.WoodenDoorBlockIndex,
            target.ExpectedData);

        Assert.Equal(target.ExpectedData, serverState.Data);
        Assert.Equal(serverState.Contents, spectatorState.Contents);
        Assert.Equal(serverState.Data, spectatorState.Data);
    }

    [Fact, TestOrder(Priority = 11)]
    public async Task TestClientPlacePathPlacesBlockForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PreparePlaceTargetAsync(server, playerIndex);
        Assert.Equal(0, target.Contents);

        await PlayerSyncTools.InjectInteractInputAsync(client, target);

        CellState serverState = await PlayerSyncTools.WaitForCellStateAsync(
            server,
            target,
            PlayerSyncTools.PlanksBlockIndex,
            target.ExpectedData);
        CellState spectatorState = await PlayerSyncTools.WaitForCellStateAsync(
            spectator,
            target,
            PlayerSyncTools.PlanksBlockIndex,
            target.ExpectedData);

        Assert.Equal(target.ExpectedContents, serverState.Contents);
        Assert.Equal(serverState.Contents, spectatorState.Contents);
        Assert.Equal(serverState.Data, spectatorState.Data);
    }

    [Fact, TestOrder(Priority = 12)]
    public async Task TestClientUsePathPaintsBlockForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PreparePaintBucketTargetAsync(server, playerIndex);
        await PlayerSyncTools.PrepareClientPlayerForActionAsync(client, target);

        await PlayerSyncTools.InjectInteractInputAsync(client, target);

        CellState serverState = await PlayerSyncTools.WaitForCellStateAsync(
            server,
            target,
            target.ExpectedContents,
            target.ExpectedData);
        CellState spectatorState = await PlayerSyncTools.WaitForCellStateAsync(
            spectator,
            target,
            target.ExpectedContents,
            target.ExpectedData);

        Assert.Equal(target.ExpectedContents, serverState.Contents);
        Assert.Equal(target.ExpectedData, serverState.Data);
        Assert.Equal(serverState.Contents, spectatorState.Contents);
        Assert.Equal(serverState.Data, spectatorState.Data);
    }

    [Fact, TestOrder(Priority = 13)]
    public async Task TestClientDigPathRemovesBlockForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PrepareDigTargetAsync(server, playerIndex);
        await PlayerSyncTools.PrepareClientPlayerForActionAsync(client, target);

        await PlayerSyncTools.InjectDigInputAsync(client, target);

        CellState serverState = await PlayerSyncTools.WaitForCellStateAsync(
            server,
            target,
            target.ExpectedContents,
            target.ExpectedData);
        CellState spectatorState = await PlayerSyncTools.WaitForCellStateAsync(
            spectator,
            target,
            target.ExpectedContents,
            target.ExpectedData);

        Assert.Equal(0, serverState.Contents);
        Assert.Equal(serverState.Contents, spectatorState.Contents);
        Assert.Equal(serverState.Data, spectatorState.Data);
    }

    [Fact, TestOrder(Priority = 14)]
    public async Task TestClientDropPathCreatesPickableForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PrepareDropTargetAsync(server, playerIndex);
        await PlayerSyncTools.PrepareClientPlayerForActionAsync(client, target, zOffset: 2f);

        int initialServerCount = await PlayerSyncTools.GetPickableCountNearAsync(server, target);
        int initialSpectatorCount = await PlayerSyncTools.GetPickableCountNearAsync(spectator, target);

        await PlayerSyncTools.InjectDropInputAsync(client);

        int finalServerCount = await PlayerSyncTools.WaitForPickableCountAtLeastAsync(server, target, initialServerCount + 1);
        int finalSpectatorCount = await PlayerSyncTools.WaitForPickableCountAtLeastAsync(spectator, target, initialSpectatorCount + 1);

        Assert.True(finalServerCount >= initialServerCount + 1, $"Server did not create dropped pickable. Target={target.Raw}");
        Assert.True(finalSpectatorCount >= initialSpectatorCount + 1, $"Spectator did not receive dropped pickable. Target={target.Raw}");
    }

    [Fact, TestOrder(Priority = 15)]
    public async Task TestClientHitPathDamagesEntityForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PrepareHitTargetAsync(server, playerIndex);
        Assert.True(target.EntityId > 0, $"Server did not create hit target. Target={target.Raw}");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(client, target.EntityId), $"Client did not receive hit target {target.EntityId}.");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(spectator, target.EntityId), $"Spectator did not receive hit target {target.EntityId}.");
        await PlayerSyncTools.PrepareClientPlayerForActionAsync(client, target, zOffset: 2f);

        await PlayerSyncTools.InjectHitInputAsync(client, target);

        DamageSyncTools.DamageEntityState serverState = await DamageSyncTools.WaitForHitpointsAtMostAsync(server, target.EntityId, 0.99f);
        DamageSyncTools.DamageEntityState spectatorState = await DamageSyncTools.WaitForDamageEntityStateAsync(
            spectator,
            target.EntityId,
            serverState.Hitpoints);

        Assert.True(serverState.Hitpoints < 1f - DamageSyncTools.HitpointsEpsilon, $"Server entity was not damaged. State={serverState}. Target={target.Raw}");
        Assert.True(DamageSyncTools.DamageStatesMatch(serverState, spectatorState), $"Spectator damage state did not match. Server={serverState}, Spectator={spectatorState}.");
    }

    [Fact, TestOrder(Priority = 16)]
    public async Task TestClientAimPathFiresProjectileForObserver() {
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        InteractionTarget target = await PlayerSyncTools.PrepareAimTargetAsync(server, playerIndex);
        await PlayerSyncTools.PrepareClientPlayerForActionAsync(client, target);

        int initialServerCount = await PlayerSyncTools.GetProjectileCountNearAsync(server, target);
        int initialSpectatorCount = await PlayerSyncTools.GetProjectileCountNearAsync(spectator, target);

        await PlayerSyncTools.InjectAimInputAsync(client, target);

        int finalServerCount = await PlayerSyncTools.WaitForProjectileCountAtLeastAsync(server, target, initialServerCount + 1);
        int finalSpectatorCount = await PlayerSyncTools.WaitForProjectileCountAtLeastAsync(spectator, target, initialSpectatorCount + 1);

        Assert.True(finalServerCount >= initialServerCount + 1, $"Server did not fire projectile. Target={target.Raw}");
        Assert.True(finalSpectatorCount >= initialSpectatorCount + 1, $"Spectator did not receive projectile. Target={target.Raw}");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
