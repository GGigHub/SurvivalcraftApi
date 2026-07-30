using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class PickableProjectileMultiplayerScenarioTest {
    [Theory]
    [InlineData(MultiplayerTransportKind.Direct)]
    [InlineData(MultiplayerTransportKind.Relay)]
    [Trait("Layer", "L2")]
    [Trait("Category", "GameplayV2")]
    [Trait("Milestone", "M5")]
    public async Task PickablesAndProjectilesSurviveAllNetworkBoundaries(
        MultiplayerTransportKind transport) {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"pickable-projectile-{transport.ToString().ToLowerInvariant()}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        PickableProjectileMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260806,
            Transport: transport);

        PickableProjectileMultiplayerScenarioResult result =
            await new PickableProjectileMultiplayerScenario().RunAsync(options);

        Assert.Equal(1, result.ActorPlayerIndex);
        Assert.Equal(3, result.ActionCount);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "actions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "attestations.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "assertions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay-v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-projectile-generated.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-a-lifecycle-final.v2.json")));
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
        Assert.Equal(PickableProjectileMultiplayerScenario.ScenarioVersion, replay.Version);
        Assert.NotNull(replay.NetworkFaults);
        Assert.Equal(1, replay.NetworkFaults.DuplicationRate);
        Assert.Equal(0.5, replay.NetworkFaults.ReorderRate);
        string assertions = await File.ReadAllTextAsync(Path.Combine(
            result.ArtifactDirectory,
            "assertions.json"));
        Assert.Contains("pickable-projectile-authority-enforcement", assertions, StringComparison.Ordinal);
        Assert.Contains("pickable-live-create-convergence", assertions, StringComparison.Ordinal);
        Assert.Contains("pickable-attraction-production-convergence", assertions, StringComparison.Ordinal);
        Assert.Contains("projectile-production-input-and-deduplication", assertions, StringComparison.Ordinal);
        Assert.Contains("pickable-stale-create-resurrection-sentry", assertions, StringComparison.Ordinal);
        Assert.Contains("pickable-projectile-late-join-baseline", assertions, StringComparison.Ordinal);
        Assert.Contains("pickable-projectile-reconnect-baseline", assertions, StringComparison.Ordinal);
        Assert.Contains("pickable-save-reload-and-projectile-transience", assertions, StringComparison.Ordinal);
        if (transport == MultiplayerTransportKind.Relay) {
            Assert.False(string.IsNullOrWhiteSpace(result.RoomCode));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "relay.log")));
        }
    }
}
