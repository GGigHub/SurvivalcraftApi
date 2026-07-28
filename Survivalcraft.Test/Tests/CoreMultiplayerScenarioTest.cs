using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CoreMultiplayerAutomationCollection {
    public const string Name = "CoreMultiplayerAutomation";
}

[Collection(CoreMultiplayerAutomationCollection.Name)]
public sealed class CoreMultiplayerScenarioTest {
    [Fact]
    [Trait("Layer", "L2")]
    [Trait("Category", "MultiplayerCore")]
    public async Task ConnectMutateReconnectAndReloadIsFullyAutomated() {
        string artifactBase = Environment.GetEnvironmentVariable("SC_TEST_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults");
        string artifactDirectory = Path.Combine(
            artifactBase,
            $"core-multiplayer-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..52]);
        CoreMultiplayerScenarioOptions options = new(
            RuntimeDirectory: AppContext.BaseDirectory,
            ArtifactDirectory: artifactDirectory,
            Seed: 20260728);

        CoreMultiplayerScenarioResult result = await new CoreMultiplayerScenario()
            .RunAsync(options);

        Assert.True(result.PersistenceDiff.Equal);
        Assert.Equal(15, result.VerifiedCell.Contents);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "outcome.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "replay.json")));
    }
}
