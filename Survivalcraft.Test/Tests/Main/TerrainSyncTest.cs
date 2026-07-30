using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class TerrainSyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;
    private GameContext spectator => fixture.Spectator;

    private static TerrainSyncTools.TerrainChunkTarget target;

    public TerrainSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestInitialChunkSnapshotSyncsToClient() {
        await GameTools.CreateTestWorld(server, "terrain-chunk-sync");
        target = await TerrainSyncTools.PaintChunkPatternAsync(server);
        Assert.True(target.SampleY > 0, $"Server did not return a valid terrain target. State={target.Raw}");

        TerrainSyncTools.TerrainChunkFingerprint serverFingerprint =
            await TerrainSyncTools.WaitForLoadedChunkAsync(server, target);
        Assert.True(serverFingerprint.Loaded, $"Server terrain chunk was not loaded. State={serverFingerprint.Raw}");

        await TerrainSyncTools.ConnectTestServerAsync(client, server.GamePort);
        Assert.True(await TerrainSyncTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");

        (TerrainSyncTools.TerrainChunkFingerprint expectedFingerprint, TerrainSyncTools.TerrainChunkFingerprint clientFingerprint) =
            await TerrainSyncTools.WaitForMatchingChunkFingerprintAsync(server, client, target);

        if (!TerrainSyncTools.FingerprintsMatch(expectedFingerprint, clientFingerprint)) {
            string diagnostics = await TerrainSyncTools.BuildTerrainSyncDiagnosticsReportAsync(server, client, target);
            Assert.Fail($"Initial chunk snapshot did not sync. Server={expectedFingerprint.Raw}, Client={clientFingerprint.Raw}{Environment.NewLine}{diagnostics}");
        }
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestCellChangeAfterChunkLoadSyncsToClient() {
        Assert.True(target.SampleY > 0, "Terrain chunk target was not initialized by the first test.");

        TerrainSyncTools.TerrainCellState changed = await TerrainSyncTools.ChangePatternCellAsync(server, target);
        Assert.True(changed.Value != 0, $"Server did not change the terrain cell. State={changed.Raw}");

        TerrainSyncTools.TerrainChunkFingerprint serverFingerprint =
            await TerrainSyncTools.WaitForLoadedChunkAsync(server, target);
        Assert.Equal(changed.Value, serverFingerprint.V2);

        TerrainSyncTools.TerrainChunkFingerprint clientFingerprint =
            await TerrainSyncTools.WaitForChunkCellSamplesAsync(client, target, serverFingerprint);

        if (!TerrainSyncTools.CellSamplesMatch(serverFingerprint, clientFingerprint)) {
            string diagnostics = await TerrainSyncTools.BuildTerrainSyncDiagnosticsReportAsync(server, client, target);
            Assert.Fail($"Incremental cell change did not sync. Server={serverFingerprint.Raw}, Client={clientFingerprint.Raw}{Environment.NewLine}{diagnostics}");
        }
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestLateJoinerReceivesModifiedChunkSnapshot() {
        Assert.True(target.SampleY > 0, "Terrain chunk target was not initialized by the first test.");

        TerrainSyncTools.TerrainChunkFingerprint serverFingerprint =
            await TerrainSyncTools.WaitForLoadedChunkAsync(server, target);
        Assert.True(serverFingerprint.Loaded, $"Server terrain chunk was not loaded. State={serverFingerprint.Raw}");

        await TerrainSyncTools.ConnectSpectatorAsync(spectator, server.GamePort);

        TerrainSyncTools.TerrainChunkFingerprint spectatorFingerprint =
            await TerrainSyncTools.WaitForChunkFingerprintAsync(spectator, target, serverFingerprint);

        if (!TerrainSyncTools.FingerprintsMatch(serverFingerprint, spectatorFingerprint)) {
            string diagnostics = await TerrainSyncTools.BuildTerrainSyncDiagnosticsReportAsync(server, client, target, spectator);
            Assert.Fail($"Late joiner did not receive the modified chunk snapshot. Server={serverFingerprint.Raw}, Spectator={spectatorFingerprint.Raw}{Environment.NewLine}{diagnostics}");
        }
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
