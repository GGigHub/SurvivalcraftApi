using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class CompatibilityMatrixScenarioTest {
    [Fact]
    [Trait("Layer", "L2")]
    [Trait("Category", "NetworkCompatibility")]
    public async Task RejectsIncompatibleClientsAndRecoversWithoutManualInput() {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"compatibility-matrix-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..56]);
        CompatibilityMatrixScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260728);

        CompatibilityMatrixScenarioResult result =
            await new CompatibilityMatrixScenario().RunAsync(options);

        Assert.Equal("ProtocolMismatch", result.RejectionCodes["protocol"]);
        Assert.Equal("ApiVersionMismatch", result.RejectionCodes["api"]);
        Assert.Equal("MissingCapabilities", result.RejectionCodes["capability"]);
        Assert.Equal("ExtraClientMod", result.RejectionCodes["mod"]);
        Assert.Equal("HandshakeTimeout", result.RejectionCodes["timeout"]);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "outcome.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "snapshot-timeout-rejected.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "snapshot-matching-accepted.json")));
    }
}
