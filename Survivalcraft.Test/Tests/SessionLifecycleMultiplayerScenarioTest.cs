using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class SessionLifecycleMultiplayerScenarioTest {
    [Theory]
    [InlineData(MultiplayerTransportKind.Direct)]
    [InlineData(MultiplayerTransportKind.Relay)]
    [Trait("Layer", "L2")]
    [Trait("Category", "SessionLifecycle")]
    [Trait("Milestone", "M6")]
    public async Task SessionsAndModChannelsSurviveProductionLifecycleBoundaries(
        MultiplayerTransportKind transport) {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"session-lifecycle-{transport.ToString().ToLowerInvariant()}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        SessionLifecycleMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Transport: transport,
            Seed: 20260730);

        SessionLifecycleMultiplayerScenarioResult result =
            await new SessionLifecycleMultiplayerScenario().RunAsync(options);

        Assert.Equal(5, result.CompatibilityRejections);
        Assert.Equal(4, result.StableIdentityCount);
        Assert.True(result.DuplicatePlayerCreatesIgnored >= 9);
        Assert.True(result.RateLimitedMessages >= 32);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "assertions.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay-v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-duplicate-identity-superseded.v2.json")));
        Assert.True(File.Exists(Path.Combine(
            result.ArtifactDirectory,
            "snapshot-server-final-session-cleanup.v2.json")));
        string assertions = await File.ReadAllTextAsync(Path.Combine(
            result.ArtifactDirectory,
            "assertions.json"));
        Assert.Contains("duplicate-player-create-idempotent", assertions, StringComparison.Ordinal);
        Assert.Contains("mod-channel-rate-limit-and-recovery", assertions, StringComparison.Ordinal);
        Assert.Contains("duplicate-identity-latest-session-wins", assertions, StringComparison.Ordinal);
    }
}
