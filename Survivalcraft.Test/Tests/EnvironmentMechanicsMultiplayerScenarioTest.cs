using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class EnvironmentMechanicsMultiplayerScenarioTest {
    [Theory]
    [InlineData(MultiplayerTransportKind.Direct)]
    [InlineData(MultiplayerTransportKind.Relay)]
    [Trait("Layer", "L2")]
    [Trait("Category", "GameplayV2")]
    [Trait("Milestone", "M1")]
    public async Task EnvironmentAndMechanicsConvergeAcrossAllNetworkBoundaries(
        MultiplayerTransportKind transport) {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"environment-mechanics-{transport.ToString().ToLowerInvariant()}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        EnvironmentMechanicsMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260803,
            Transport: transport);

        EnvironmentMechanicsMultiplayerScenarioResult result =
            await new EnvironmentMechanicsMultiplayerScenario().RunAsync(options);

        Assert.Equal(1, result.ActorPlayerIndex);
        Assert.Equal(8, result.ActionCount);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "actions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "assertions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay-v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-survival-authority-fixture.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-after-noncreative-environment.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-observer-after-noncreative-environment.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-fault-convergence.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-c-late-join.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-a-reconnect.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-after-reload.v2.json")));
        ScenarioReplayV2 replay = await ScenarioArtifactWriter.ReadReplayV2Async(
            result.ArtifactDirectory);
        Assert.Equal(EnvironmentMechanicsMultiplayerScenario.ScenarioVersion, replay.Version);
        Assert.NotNull(replay.NetworkFaults);
        Assert.Equal(1, replay.NetworkFaults.DuplicationRate);
        Assert.Equal(0.5, replay.NetworkFaults.ReorderRate);
        string assertions = await File.ReadAllTextAsync(Path.Combine(
            result.ArtifactDirectory,
            "assertions.json"));
        Assert.Contains("environment-mechanics-control-authority-enforcement", assertions,
            StringComparison.Ordinal);
        Assert.Contains("environment-sleep-fire-production-authority", assertions,
            StringComparison.Ordinal);
        Assert.Contains("environment-mechanics-duplicate-reorder-convergence", assertions,
            StringComparison.Ordinal);
        Assert.Contains("environment-mechanics-late-join-baseline", assertions,
            StringComparison.Ordinal);
        Assert.Contains("environment-mechanics-reconnect-baseline", assertions,
            StringComparison.Ordinal);
        Assert.Contains("environment-mechanics-save-reload-persistence", assertions,
            StringComparison.Ordinal);
        if (transport == MultiplayerTransportKind.Relay) {
            Assert.False(string.IsNullOrWhiteSpace(result.RoomCode));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "relay.log")));
        }
    }
}
