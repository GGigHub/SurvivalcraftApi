using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class CreatureSyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public CreatureSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestExistingEntitiesSyncWhenClientJoinsGame() {
        await GameTools.CreateTestWorld(server);

        int liveEntityId = await EntitySyncTools.CreateBoatAsync(server, 4f, 2f, 4f);
        Assert.True(liveEntityId > 0, "Server did not create the pre-existing entity.");

        int removedEntityId = await EntitySyncTools.CreateAndRemoveBoatImmediatelyAsync(server, 6f, 2f, 4f);
        Assert.True(removedEntityId > 0, "Server did not create the pre-removed entity.");
        Assert.True(await EntitySyncTools.WaitForEntityMissingAsync(server, removedEntityId), $"Server still has pre-removed entity {removedEntityId}.");

        await GameTools.ConnectTestServer(client, server.GamePort);

        (EntitySyncState serverState, EntitySyncState clientState) =
            await EntitySyncTools.WaitForEntityStatesMatchAsync(server, client, liveEntityId);

        Assert.True(
            EntitySyncTools.StatesMatch(serverState, clientState),
            $"Client did not receive the pre-existing entity during initial entity load. Server={serverState}, Client={clientState}.");
        Assert.True(
            await EntitySyncTools.EntityStaysMissingOrNotBoatAsync(client, removedEntityId),
            $"Client received removed Boat entity {removedEntityId} during initial entity load.");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestEntityAddSyncsToConnectedClient() {
        int entityId = await EntitySyncTools.CreateBoatAsync(server, 8f, 2f, 4f);
        Assert.True(entityId > 0, "Server did not create the entity.");

        (EntitySyncState serverState, EntitySyncState clientState) =
            await EntitySyncTools.WaitForEntityStatesMatchAsync(server, client, entityId);

        Assert.True(
            EntitySyncTools.StatesMatch(serverState, clientState),
            $"Client did not receive added entity {entityId}. Server={serverState}, Client={clientState}.");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestEntityRemoveSyncsToConnectedClient() {
        int entityId = await EntitySyncTools.CreateBoatAsync(server, 10f, 2f, 4f);
        Assert.True(entityId > 0, "Server did not create the entity to remove.");

        (EntitySyncState serverState, EntitySyncState clientState) =
            await EntitySyncTools.WaitForEntityStatesMatchAsync(server, client, entityId);
        Assert.True(
            EntitySyncTools.StatesMatch(serverState, clientState),
            $"Client did not receive entity {entityId} before removal. Server={serverState}, Client={clientState}.");

        int removedId = await EntitySyncTools.RemoveEntityAsync(server, entityId);
        Assert.Equal(entityId, removedId);

        Assert.True(
            await EntitySyncTools.WaitForEntityMissingAsync(client, entityId),
            $"Client still has removed entity {entityId}.");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestEntityAddThenImmediateRemoveDoesNotResurrectOnClient() {
        int entityId = await EntitySyncTools.CreateAndRemoveBoatImmediatelyAsync(server, 12f, 2f, 4f);
        Assert.True(entityId > 0, "Server did not create and remove the entity.");
        Assert.True(await EntitySyncTools.WaitForEntityMissingAsync(server, entityId), $"Server still has removed entity {entityId}.");

        Assert.True(
            await EntitySyncTools.EntityStaysMissingAsync(client, entityId),
            $"Client resurrected entity {entityId} after an add-then-remove sequence.");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestServerBodyPositionUpdateSyncsToClient() {
        int playerIndex = await DamageSyncTools.WaitForClientPlayerIndexAsync(client);
        await CreatureSyncTools.WaitForPlayerBodyAsync(server, playerIndex);

        int entityId = await CreatureSyncTools.CreateBoatNearPlayerAsync(server, playerIndex, 2f, 8f, 2f);
        Assert.True(entityId > 0, "Server did not create the body test boat.");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive body entity {entityId}.");

        BodySyncState serverState = await CreatureSyncTools.OffsetBodyPositionAsync(
            server,
            entityId,
            new Vector3Dto(1.25f, 0f, 0.5f),
            new Vector3Dto(0f, 0f, 0f));
        Assert.True(serverState.Exists, $"Server body was not available after position update. State={serverState}.");

        BodySyncState clientState = await CreatureSyncTools.WaitForBodyStateNearAsync(client, entityId, serverState);

        Assert.True(
            CreatureSyncTools.BodyStatesNear(serverState, clientState),
            $"Body position update did not synchronize to client. Server={serverState}, Client={clientState}.");
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task TestClientMainPlayerBodyPositionUpdateSyncsToServer() {
        int playerIndex = await DamageSyncTools.WaitForClientPlayerIndexAsync(client);

        BodySyncState clientState = await CreatureSyncTools.OffsetMainPlayerBodyPositionAsync(
            client,
            new Vector3Dto(1.15f, 0f, -0.4f),
            new Vector3Dto(0f, 0f, 0f));
        Assert.True(clientState.Exists, $"Client main player body was not available. State={clientState}.");

        BodySyncState serverState = await CreatureSyncTools.WaitForPlayerHorizontalPositionNearAsync(
            server,
            playerIndex,
            clientState,
            CreatureSyncTools.PlayerPositionEpsilonSquared);

        Assert.True(
            CreatureSyncTools.PlayerBodyHorizontalPositionsNear(clientState, serverState),
            $"Main player body position update did not synchronize to server. Client={clientState}, Server={serverState}.");
    }

    [Fact, TestOrder(Priority = 7)]
    public async Task TestClientPlayerBodyCollisionIsAcceptedByServer() {
        int playerIndex = await DamageSyncTools.WaitForClientPlayerIndexAsync(client);
        await CreatureSyncTools.WaitForPlayerBodyAsync(client, playerIndex);
        await CreatureSyncTools.WaitForPlayerBodyAsync(server, playerIndex);
        int targetId = await CreatureSyncTools.CreateBoatNearPlayerAsync(server, playerIndex, 1f, 0.5f, 0f);
        Assert.True(targetId > 0, "Server did not create the target boat.");
        Assert.True(await DamageSyncTools.WaitForEntityExistsAsync(client, targetId), $"Client did not receive target boat {targetId}.");

        BodySyncState preparedClientState = await CreatureSyncTools.PrepareMainPlayerForBodyCollisionAsync(
            client,
            targetId,
            new Vector3Dto(-0.45f, -0.5f, 0f),
            new Vector3Dto(0f, 0f, 0f));
        Assert.True(preparedClientState.Exists, $"Client did not prepare the main player body. State={preparedClientState}.");

        BodySyncState preparedServerState = await CreatureSyncTools.WaitForPlayerHorizontalPositionNearAsync(
            server,
            playerIndex,
            preparedClientState,
            CreatureSyncTools.CollisionPreparationPositionEpsilonSquared);
        Assert.True(
            Vector3Dto.HorizontalDistanceSquared(preparedClientState.Position, preparedServerState.Position)
                <= CreatureSyncTools.CollisionPreparationPositionEpsilonSquared,
            $"Main player body preparation did not synchronize to server. Client={preparedClientState}, Server={preparedServerState}.");

        BodySyncState initialServerTargetState = await CreatureSyncTools.GetBodyStateAsync(server, targetId);
        Assert.True(initialServerTargetState.Exists, $"Server target boat body was not available. State={initialServerTargetState}.");
        await CreatureSyncTools.WaitForBodyStateNearAsync(client, targetId, initialServerTargetState);

        BodySyncState collisionClientState = await CreatureSyncTools.OffsetMainPlayerBodyPositionAsync(
            client,
            new Vector3Dto(0.3f, 0f, 0f),
            new Vector3Dto(1f, 0f, 0f));
        Assert.True(collisionClientState.Exists, $"Client did not nudge the main player body into the target. State={collisionClientState}.");

        await CreatureSyncTools.HoldMainPlayerMoveInputAsync(client, new Vector3Dto(1f, 0f, 0f), 90);

        BodySyncState collisionServerState = await CreatureSyncTools.WaitForPlayerHorizontalPositionNearAsync(
            server,
            playerIndex,
            collisionClientState,
            CreatureSyncTools.CollisionPreparationPositionEpsilonSquared);
        BodySyncState serverTargetState = await CreatureSyncTools.GetBodyStateAsync(server, targetId);
        BodySyncState clientTargetState = await CreatureSyncTools.WaitForBodyStateNearAsync(client, targetId, serverTargetState);

        Assert.True(
            Vector3Dto.HorizontalDistanceSquared(collisionClientState.Position, collisionServerState.Position)
                <= CreatureSyncTools.CollisionPreparationPositionEpsilonSquared
            && CreatureSyncTools.BodyStatesNear(serverTargetState, clientTargetState),
            $"Server did not accept the client body update near the target boat, or target boat state diverged. ClientPlayer={collisionClientState}, ServerPlayer={collisionServerState}, ServerTarget={serverTargetState}, ClientTarget={clientTargetState}.");
    }

    [Fact, TestOrder(Priority = 8)]
    public async Task TestCreatureSoundsAreDerivedLocallyOnEachPeer() {
        Dictionary<string, int> entityIds = await CreatureSyncTools.AddBehaviorSyncEntitiesAsync(server);
        Assert.True(entityIds.TryGetValue("Sound", out int entityId) && entityId > 0, "Server did not create a creature with sounds.");
        Assert.True(await CreatureSyncTools.WaitForEntityExistsAsync(client, entityId), $"Client did not receive sound test entity {entityId}.");

        foreach (CreatureSoundType soundType in Enum.GetValues<CreatureSoundType>()) {
            await CreatureSyncTools.ConfigureCreatureSoundStateAsync(server, entityId);
            await CreatureSyncTools.ConfigureCreatureSoundStateAsync(client, entityId);
            await CreatureSyncTools.TriggerCreatureSoundAsync(server, entityId, soundType);

            CreatureSoundState serverState = await CreatureSyncTools.GetCreatureSoundStateAsync(server, entityId, soundType);
            Assert.True(serverState.Played, $"Server did not play {soundType} creature sound. State={serverState}.");

            CreatureSoundState clientBeforeLocalTrigger = await CreatureSyncTools.GetCreatureSoundStateAsync(
                client,
                entityId,
                soundType);
            Assert.False(
                clientBeforeLocalTrigger.Played,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Server-local {soundType} sound unexpectedly mutated the client state. "
                    + $"Server={serverState}, Client={clientBeforeLocalTrigger}."));

            await CreatureSyncTools.TriggerCreatureSoundAsync(client, entityId, soundType);
            CreatureSoundState clientState = await CreatureSyncTools.WaitForCreatureSoundPlayedAsync(client, entityId, soundType);
            Assert.True(
                clientState.Played,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Client did not derive {soundType} creature sound locally for entity {entityId}. "
                    + $"Server={serverState}, Client={clientState}."));
        }
    }

    [Fact, TestOrder(Priority = 9)]
    public async Task TestRequestedCreatureBehaviorStateSync() {
        Dictionary<string, int> entityIds = await CreatureSyncTools.AddBehaviorSyncEntitiesAsync(server);
        string[] requiredBehaviors = [
            "Chase",
            "DigInMud",
            "EatPickable",
            "FishOutOfWater",
            "LookAround",
            "RandomFeed",
            "RandomPeck",
            "StubbornSteed"
        ];

        foreach (string behaviorName in requiredBehaviors) {
            Assert.True(
                entityIds.TryGetValue(behaviorName, out int entityId) && entityId > 0,
                $"Server did not create or locate an entity for {behaviorName}.");
        }

        await CreatureSyncTools.WaitForEntitiesAsync(client, requiredBehaviors.Select(name => entityIds[name]));

        BehaviorSyncState serverState = await CreatureSyncTools.ForceBehaviorSyncStateAsync(server, entityIds);
        CreatureSyncTools.AssertExpectedBehaviorSyncState(serverState, "Server");

        BehaviorSyncState clientState = await CreatureSyncTools.WaitForBehaviorSyncStateAsync(client, entityIds);
        Assert.True(
            CreatureSyncTools.HasExpectedBehaviorSyncState(clientState),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Client did not receive requested creature behavior sync state. Server={serverState}, Client={clientState}."));
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
