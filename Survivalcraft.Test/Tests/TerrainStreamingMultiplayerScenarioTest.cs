using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class TerrainStreamingMultiplayerScenarioTest {
    [Theory]
    [InlineData(MultiplayerTransportKind.Direct)]
    [InlineData(MultiplayerTransportKind.Relay)]
    [Trait("Layer", "L2")]
    [Trait("Category", "GameplayV2")]
    [Trait("Milestone", "M1")]
    public async Task StreamingRevisionsSurviveFaultsLateJoinReconnectAndReload(
        MultiplayerTransportKind transport) {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"terrain-streaming-{transport.ToString().ToLowerInvariant()}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        TerrainStreamingMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260731,
            Transport: transport);

        TerrainStreamingMultiplayerScenarioResult result =
            await new TerrainStreamingMultiplayerScenario().RunAsync(options);

        Assert.Equal(1, result.ActorPlayerIndex);
        Assert.Equal(2, result.ActionCount);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "actions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "assertions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay-v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-a-before-boundary.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-a-after-boundary-move.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-c-after-late-join.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-a-after-reconnect.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-after-reload.v2.json")));
        ScenarioReplayV2 replay = await ScenarioArtifactWriter.ReadReplayV2Async(
            result.ArtifactDirectory);
        Assert.Equal(TerrainStreamingMultiplayerScenario.ScenarioVersion, replay.Version);
        Assert.NotNull(replay.NetworkFaults);
        Assert.Equal(1, replay.NetworkFaults.DuplicationRate);
        string assertions = await File.ReadAllTextAsync(Path.Combine(
            result.ArtifactDirectory,
            "assertions.json"));
        Assert.Contains("terrain-loadedness-boundary", assertions, StringComparison.Ordinal);
        Assert.Contains("terrain-streaming-authority-enforcement", assertions, StringComparison.Ordinal);
        Assert.Contains("terrain-production-streaming-after-player-move", assertions, StringComparison.Ordinal);
        Assert.Contains("terrain-revision-stale-duplicate-reorder-convergence", assertions, StringComparison.Ordinal);
        Assert.Contains("terrain-streaming-late-join-baseline", assertions, StringComparison.Ordinal);
        Assert.Contains("terrain-streaming-reconnect-baseline", assertions, StringComparison.Ordinal);
        Assert.Contains("terrain-save-reload-revision-boundary", assertions, StringComparison.Ordinal);
        if (transport == MultiplayerTransportKind.Relay) {
            Assert.False(string.IsNullOrWhiteSpace(result.RoomCode));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "relay.log")));
        }
    }
}
