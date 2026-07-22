using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class TerrainStreamingTest : IClassFixture<GameFixture> {
    private const float ClientMoveDistance = 80f;

    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    private static int clientPlayerIndex = -1;
    private static ChunkLoadSnapshot serverBeforeClientJoin;
    private static ChunkLoadSnapshot clientInitialSnapshot;
    private static ChunkLoadSnapshot serverInitialSnapshot;

    public TerrainStreamingTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestClientInitialLoadedChunkCountMatchesServerAt64Blocks() {
        await TerrainStreamingTools.SetVisibilityDistanceAsync(server);
        await TerrainStreamingTools.SetVisibilityDistanceAsync(client);
        await GameTools.CreateTestWorld(server, "terrain-streaming-64");

        ChunkLoadSnapshot drawingLocation = await TerrainStreamingTools.WaitForExpectedLocationAsync(server);
        Assert.True(TerrainStreamingTools.IsAtExpectedLocation(drawingLocation),
            $"Server did not enter the 64-block terrain drawing update path. State={drawingLocation.Raw}");
        await Task.Delay(TimeSpan.FromSeconds(12));

        serverBeforeClientJoin = await TerrainStreamingTools.WaitForFullyLoadedRadiusAsync(server);
        Assert.True(TerrainStreamingTools.IsAtExpectedLocation(serverBeforeClientJoin),
            $"Server did not load a 64-block radius 12 seconds after terrain drawing began. Total={serverBeforeClientJoin.TotalAllocatedChunkCount}/{serverBeforeClientJoin.TotalLoadedChunkCount}; InRange={serverBeforeClientJoin.AllocatedChunkCount}/{serverBeforeClientJoin.LoadedChunkCount}; State={serverBeforeClientJoin.Raw}");

        await GameTools.ConnectTestServer(client, server.GamePort);
        clientPlayerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(clientPlayerIndex >= 0, "Client player index was not assigned.");

        serverInitialSnapshot = await TerrainStreamingTools.WaitForFullyLoadedRadiusAsync(server, clientPlayerIndex);
        clientInitialSnapshot = await TerrainStreamingTools.WaitForFullyLoadedRadiusAsync(client, clientPlayerIndex);

        Assert.True(TerrainStreamingTools.IsAtExpectedLocation(serverInitialSnapshot),
            $"Server did not receive the client's 64-block terrain update location. State={serverInitialSnapshot.Raw}");
        Assert.True(TerrainStreamingTools.IsAtExpectedLocation(clientInitialSnapshot),
            $"Client did not maintain a 64-block terrain update location. State={clientInitialSnapshot.Raw}");
        Assert.True(serverInitialSnapshot.LoadedChunkCount == clientInitialSnapshot.LoadedChunkCount,
            $"Initial effective chunk count differs. Server={serverInitialSnapshot.LoadedChunkCount} at {serverInitialSnapshot.UpdateCenter}; Client={clientInitialSnapshot.LoadedChunkCount} at {clientInitialSnapshot.UpdateCenter}.");
        Assert.True(serverInitialSnapshot.LoadedChunkCoordinates.SetEquals(clientInitialSnapshot.LoadedChunkCoordinates),
            $"Initial effective chunk coordinates differ. Server={serverInitialSnapshot.Raw}; Client={clientInitialSnapshot.Raw}");
        Assert.True(serverInitialSnapshot.LoadedChunkCount >= clientInitialSnapshot.LoadedChunkCount,
            $"Server did not have all chunks needed by the joining client. Server={serverInitialSnapshot.Raw}, Client={clientInitialSnapshot.Raw}");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestClientMovementLoadsNewChunksAtTheViewDistanceEdge() {
        Assert.True(clientPlayerIndex >= 0, "Initial terrain streaming test did not initialize the client player.");

        ChunkLoadSnapshot serverBeforeMove = await TerrainStreamingTools.GetChunkLoadSnapshotAsync(server);
        BodySyncState movedClientBody = await CreatureSyncTools.OffsetMainPlayerBodyPositionAsync(
            client,
            new Vector3Dto(ClientMoveDistance, 0f, 0f),
            new Vector3Dto(0f, 0f, 0f));
        Assert.True(movedClientBody.Exists, $"Client main player body was not available. State={movedClientBody}.");

        BodySyncState serverClientBody = await CreatureSyncTools.WaitForPlayerNearAsync(server, clientPlayerIndex, movedClientBody);
        Assert.True(CreatureSyncTools.PlayerBodyStatesNear(movedClientBody, serverClientBody),
            $"Server did not receive the client movement. Client={movedClientBody}, Server={serverClientBody}.");

        ChunkLoadSnapshot movedClientSnapshot = await TerrainStreamingTools.WaitForExpectedLocationAsync(client, clientPlayerIndex);
        Assert.True(TerrainStreamingTools.IsAtExpectedLocation(movedClientSnapshot),
            $"Client terrain update location did not follow the moved player. State={movedClientSnapshot.Raw}");

        (int targetChunkX, int targetChunkZ) = TerrainStreamingTools.GetForwardEdgeChunk(movedClientSnapshot);
        Task<ChunkLoadState> serverTargetTask = TerrainStreamingTools.WaitForLoadedChunkAsync(server, targetChunkX, targetChunkZ);
        Task<ChunkLoadState> clientTargetTask = TerrainStreamingTools.WaitForLoadedChunkAsync(client, targetChunkX, targetChunkZ);
        await Task.WhenAll(serverTargetTask, clientTargetTask);
        ChunkLoadState serverTarget = await serverTargetTask;
        ChunkLoadState clientTarget = await clientTargetTask;
        ChunkLoadSnapshot serverAfterMove = await TerrainStreamingTools.GetChunkLoadSnapshotAsync(server);

        Assert.True(serverTarget.Loaded,
            $"Server did not load the chunk at the moved client's 64-block view-distance edge. Target={targetChunkX},{targetChunkZ}; State={serverTarget.Raw}");
        Assert.True(clientTarget.Loaded,
            $"Client did not receive the newly visible edge chunk. Target={targetChunkX},{targetChunkZ}; State={clientTarget.Raw}");
        Assert.True(Vector3Dto.HorizontalDistanceSquared(serverBeforeMove.BodyPosition, serverAfterMove.BodyPosition) < 0.01f,
            $"Server player moved while only the client was moved. Before={serverBeforeMove.Raw}, After={serverAfterMove.Raw}");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestClientLargerViewDistancesAreServerDriven() {
        Assert.True(clientPlayerIndex >= 0, "Initial terrain streaming test did not initialize the client player.");

        foreach (float clientVisibilityDistance in new[] { 128f, 256f }) {
            await TerrainStreamingTools.SetVisibilityDistanceAsync(client, clientVisibilityDistance);

            ChunkLoadSnapshot serverLocation = await TerrainStreamingTools.WaitForExpectedLocationAsync(server, clientPlayerIndex, clientVisibilityDistance);
            ChunkLoadSnapshot clientLocation = await TerrainStreamingTools.WaitForExpectedLocationAsync(client, clientPlayerIndex, clientVisibilityDistance);
            Assert.True(TerrainStreamingTools.IsAtExpectedLocation(serverLocation, clientVisibilityDistance),
                $"Server did not receive the client's {clientVisibilityDistance}-block terrain update location. State={serverLocation.Raw}");
            Assert.True(TerrainStreamingTools.IsAtExpectedLocation(clientLocation, clientVisibilityDistance),
                $"Client did not update to {clientVisibilityDistance}-block terrain view distance. State={clientLocation.Raw}");

            Task<ChunkLoadSnapshot> serverLoadedTask = TerrainStreamingTools.WaitForFullyLoadedRadiusAsync(server, clientPlayerIndex, clientVisibilityDistance, attempts: 600);
            Task<ChunkLoadSnapshot> clientLoadedTask = TerrainStreamingTools.WaitForFullyLoadedRadiusAsync(client, clientPlayerIndex, clientVisibilityDistance, attempts: 600);
            await Task.WhenAll(serverLoadedTask, clientLoadedTask);
            ChunkLoadSnapshot serverLoaded = await serverLoadedTask;
            ChunkLoadSnapshot clientLoaded = await clientLoadedTask;

            Assert.True(serverLoaded.LoadedChunkCoordinates.SetEquals(clientLoaded.LoadedChunkCoordinates),
                $"Server-driven {clientVisibilityDistance}-block chunk coordinates differ. Server={serverLoaded.Raw}; Client={clientLoaded.Raw}");

            ChunkLoadSnapshot serverBeforeMove = await TerrainStreamingTools.GetChunkLoadSnapshotAsync(server);
            BodySyncState movedClientBody = await CreatureSyncTools.OffsetMainPlayerBodyPositionAsync(
                client,
                new Vector3Dto(clientVisibilityDistance + 16f, 0f, 0f),
                new Vector3Dto(0f, 0f, 0f));
            BodySyncState serverClientBody = await CreatureSyncTools.WaitForPlayerNearAsync(server, clientPlayerIndex, movedClientBody);
            Assert.True(CreatureSyncTools.PlayerBodyStatesNear(movedClientBody, serverClientBody),
                $"Server did not receive client movement at {clientVisibilityDistance}-block view distance. Client={movedClientBody}, Server={serverClientBody}.");

            ChunkLoadSnapshot movedClientLocation = await TerrainStreamingTools.WaitForExpectedLocationAsync(client, clientPlayerIndex, clientVisibilityDistance);
            (int targetChunkX, int targetChunkZ) = TerrainStreamingTools.GetForwardEdgeChunk(movedClientLocation, clientVisibilityDistance);
            Task<ChunkLoadState> serverTargetTask = TerrainStreamingTools.WaitForLoadedChunkAsync(server, targetChunkX, targetChunkZ);
            Task<ChunkLoadState> clientTargetTask = TerrainStreamingTools.WaitForLoadedChunkAsync(client, targetChunkX, targetChunkZ);
            await Task.WhenAll(serverTargetTask, clientTargetTask);
            ChunkLoadSnapshot serverAfterMove = await TerrainStreamingTools.GetChunkLoadSnapshotAsync(server);

            Assert.True((await serverTargetTask).Loaded,
                $"Server did not load the {clientVisibilityDistance}-block edge chunk {targetChunkX},{targetChunkZ}.");
            Assert.True((await clientTargetTask).Loaded,
                $"Client did not receive the {clientVisibilityDistance}-block edge chunk {targetChunkX},{targetChunkZ}.");
            Assert.True(Vector3Dto.HorizontalDistanceSquared(serverBeforeMove.BodyPosition, serverAfterMove.BodyPosition) < 0.01f,
                $"Server player moved while only the client moved at {clientVisibilityDistance}-block view distance.");
        }
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
