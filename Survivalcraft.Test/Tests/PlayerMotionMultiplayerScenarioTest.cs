using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class PlayerMotionMultiplayerScenarioTest {
    [Theory]
    [InlineData(MultiplayerTransportKind.Direct)]
    [InlineData(MultiplayerTransportKind.Relay)]
    [Trait("Layer", "L2")]
    [Trait("Category", "GameplayV2")]
    [Trait("Milestone", "M1")]
    public async Task MotionLookCrouchSurviveLateJoinReconnectFaultsAndReload(
        MultiplayerTransportKind transport) {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"player-motion-{transport.ToString().ToLowerInvariant()}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        PlayerMotionMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260730,
            Transport: transport);

        PlayerMotionMultiplayerScenarioResult result =
            await new PlayerMotionMultiplayerScenario().RunAsync(options);

        Assert.Equal(1, result.ActorPlayerIndex);
        Assert.Equal(5, result.ActionCount);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "actions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "assertions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay-v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-b-crouch-final.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-c-after-late-join.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-a-after-reconnect.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-after-reload.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-c-persistence-observer.v2.json")));
        ScenarioReplayV2 replay = await ScenarioArtifactWriter.ReadReplayV2Async(
            result.ArtifactDirectory);
        Assert.Equal(PlayerMotionMultiplayerScenario.ScenarioVersion, replay.Version);
        Assert.NotNull(replay.NetworkFaults);
        Assert.Equal(1, replay.NetworkFaults.DuplicationRate);
        string assertions = await File.ReadAllTextAsync(Path.Combine(
            result.ArtifactDirectory,
            "assertions.json"));
        Assert.Contains("move-authority-observer-convergence", assertions, StringComparison.Ordinal);
        Assert.Contains("look-authority-observer-convergence", assertions, StringComparison.Ordinal);
        Assert.Contains("crouch-authority-observer-convergence", assertions, StringComparison.Ordinal);
        Assert.Contains("motion-late-join-convergence", assertions, StringComparison.Ordinal);
        Assert.Contains("motion-reconnect-semantics", assertions, StringComparison.Ordinal);
        Assert.Contains("motion-action-under-network-faults", assertions, StringComparison.Ordinal);
        Assert.Contains("motion-save-reload-semantics", assertions, StringComparison.Ordinal);
        if (transport == MultiplayerTransportKind.Relay) {
            Assert.False(string.IsNullOrWhiteSpace(result.RoomCode));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "relay.log")));
        }
    }
}
