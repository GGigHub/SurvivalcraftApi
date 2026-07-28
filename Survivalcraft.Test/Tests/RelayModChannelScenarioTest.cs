using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class RelayModChannelScenarioTest {
    [Fact]
    [Trait("Layer", "L2")]
    [Trait("Category", "ModNetworkRelay")]
    public async Task NegotiatesDeduplicatesAndReconnectsWithoutManualInput() {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"relay-mod-channel-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..54]);
        RelayModChannelScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260728);

        RelayModChannelScenarioResult result = await new RelayModChannelScenario()
            .RunAsync(options);

        Assert.False(string.IsNullOrWhiteSpace(result.RoomCode));
        Assert.True(result.DeduplicatedFrames >= 1);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "relay.log")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "outcome.json")));
    }
}
