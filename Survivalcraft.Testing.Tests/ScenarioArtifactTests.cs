using System.Text.Json;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class ScenarioArtifactTests {
    [Fact]
    [Trait("Layer", "L0")]
    public async Task BundleContainsReplaySnapshotsDiffAndLogs() {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.Testing.Tests",
            Guid.NewGuid().ToString("N"));
        try {
            StateSnapshot snapshot = new(
                1,
                "server",
                1,
                0.05,
                "GameScreen",
                "Playing",
                null,
                [],
                [],
                [],
                new Dictionary<string, string>());
            ScenarioReplay replay = new(
                "core-multiplayer",
                1,
                42,
                "runtime",
                new NetworkFaultProfile(42));
            ScenarioArtifactBundle bundle = new(
                new ScenarioDefinition(
                    "core-multiplayer",
                    1,
                    42,
                    ["server", "client"],
                    [new ScenarioStepDefinition("connect", "network.connect")]),
                replay,
                [new ScenarioStepRecord(
                    "connect",
                    "passed",
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch)],
                new Dictionary<string, StateSnapshot> {
                    ["server-final"] = snapshot
                },
                new SnapshotDiff(true, "a", "a", []),
                new Dictionary<string, string[]> {
                    ["server"] = ["ready"]
                },
                new Dictionary<string, string> {
                    ["durationMs"] = "1"
                },
                "passed",
                null);

            string artifactPath = await ScenarioArtifactWriter.WriteAsync(root, bundle);
            ScenarioReplay loaded = await ScenarioArtifactWriter.ReadReplayAsync(artifactPath);

            Assert.Equal(replay, loaded);
            Assert.True(File.Exists(Path.Combine(root, "scenario.json")));
            Assert.True(File.Exists(Path.Combine(root, "snapshot-server-final.json")));
            Assert.True(File.Exists(Path.Combine(root, "state.diff.json")));
            Assert.True(File.Exists(Path.Combine(root, "server.log")));
            string[] jsonFiles = Directory.GetFiles(root, "*.json");
            Assert.NotEmpty(jsonFiles);
            foreach (string jsonFile in jsonFiles) {
                byte[] bytes = await File.ReadAllBytesAsync(jsonFile);
                Assert.NotEmpty(bytes);
                Assert.DoesNotContain((byte)0, bytes);
                using JsonDocument document = JsonDocument.Parse(bytes);
                Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
            }
            Assert.Equal(
                "ready" + Environment.NewLine,
                await File.ReadAllTextAsync(Path.Combine(root, "server.log")));
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }
        finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
