using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class TerrainInteractionMultiplayerScenarioTest {
    [Theory]
    [InlineData(MultiplayerTransportKind.Direct)]
    [InlineData(MultiplayerTransportKind.Relay)]
    [Trait("Layer", "L2")]
    [Trait("Category", "GameplayV2")]
    [Trait("Milestone", "M1")]
    public async Task PlaceDigUseConvergeAcrossAuthorityAndObserver(
        MultiplayerTransportKind transport) {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"terrain-interaction-{transport.ToString().ToLowerInvariant()}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        TerrainInteractionMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260729,
            Transport: transport);

        TerrainInteractionMultiplayerScenarioResult result =
            await new TerrainInteractionMultiplayerScenario().RunAsync(options);

        Assert.Equal(1, result.ActorPlayerIndex);
        Assert.Equal(3, result.ActionCount);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "actions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "assertions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay-v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-client-b-dig-final.v2.json")));
        if (transport == MultiplayerTransportKind.Relay) {
            Assert.False(string.IsNullOrWhiteSpace(result.RoomCode));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "relay.log")));
        }
    }
}
