using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class SurvivalStateMultiplayerScenarioTest {
    [Theory]
    [InlineData(MultiplayerTransportKind.Direct)]
    [InlineData(MultiplayerTransportKind.Relay)]
    [Trait("Layer", "L2")]
    [Trait("Category", "GameplayV2")]
    public async Task SurvivalStateConvergesAcrossAuthorityLifecycleAndPersistence(
        MultiplayerTransportKind transport) {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"survival-state-{transport.ToString().ToLowerInvariant()}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        SurvivalStateMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260809,
            Transport: transport);

        SurvivalStateMultiplayerScenarioResult result =
            await new SurvivalStateMultiplayerScenario().RunAsync(options);

        Assert.Equal(1, result.ActorPlayerIndex);
        Assert.Equal(8, result.ActionCount);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "actions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "assertions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay-v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-b-revision-convergence.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-c-late-join.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-a-server-persistence.v2.json")));
        if (transport == MultiplayerTransportKind.Relay) {
            Assert.False(string.IsNullOrWhiteSpace(result.RoomCode));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "relay.log")));
        }
    }
}
