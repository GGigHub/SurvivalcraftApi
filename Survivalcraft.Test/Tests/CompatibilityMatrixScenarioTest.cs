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
        Assert.Equal("MissingClientChannel", result.RejectionCodes["mod-channel"]);
        foreach (string phase in new[] {
                     "timeout-compatibility-hello",
                     "timeout-compatibility-result",
                     "timeout-mod-channel-catalog",
                     "timeout-mod-channel-catalog-result"
                 }) {
            Assert.Equal("HandshakeTimeout", result.RejectionCodes[phase]);
        }
        Assert.Equal("AdmissionTimeout", result.RejectionCodes["timeout-session-connect"]);
        Assert.Equal(
            "AdmissionTimeout",
            result.RejectionCodes["timeout-session-admission-result"]);
        Assert.Equal(11, result.RejectionCodes.Count);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "outcome.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-timeout-session-admission-result-rejected.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "snapshot-matching-accepted.json")));
    }
}
