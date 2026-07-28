using System.Text.Json;
using Survivalcraft.Testing.Control;
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

    [Fact]
    [Trait("Layer", "L0")]
    public async Task V2BundleWritesCapabilitiesActionsEventsAndProjectedSnapshots() {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.Testing.Tests",
            Guid.NewGuid().ToString("N"));
        try {
            AutomationDescription capabilities = new(
                ControlProtocol.CurrentVersion,
                AutomationProtocol.CurrentApiVersion,
                [1, 2],
                [],
                [],
                new AutomationLimits(
                    ControlProtocol.MaximumFrameBytes,
                    2048,
                    4096,
                    1024,
                    1024,
                    512,
                    4096),
                "test",
                true);
            ActionReceipt receipt = new("a1", true, 1, 1, "applied");
            StateSnapshotV2 snapshot = new(
                2,
                "capture",
                1,
                0.05,
                new NodeSnapshotV2("client-a", "GameScreen", true, true, "network"),
                null,
                [],
                [],
                [],
                new Dictionary<string, string>());
            ScenarioArtifactBundleV2 bundle = new(
                new ScenarioDefinition("creative-fly", 1, 42, ["server", "client-a", "client-b"], []),
                new ScenarioReplayV2("creative-fly", 1, 42, "runtime", "direct", 1, 2, 2),
                new ScenarioTopologyV2("direct", "server", ["client-a", "client-b"], false, null),
                [],
                new Dictionary<string, AutomationDescription> { ["client-a"] = capabilities },
                [new ScenarioActionRecordV2(
                    "client-a",
                    ControlCommandNames.SetCreativeFly,
                    JsonSerializer.SerializeToElement(
                        new SetCreativeFlyCommand(true, 1),
                        ControlProtocol.JsonOptions),
                    receipt)],
                new Dictionary<string, ActionEventsResult> {
                    ["client-a"] = new ActionEventsResult([], 1, 0, 0, false)
                },
                new Dictionary<string, StateSnapshotV2> { ["client-a-final"] = snapshot },
                [new ScenarioAssertionRecordV2("fly", "passed", ["client-a"], "enabled")],
                new Dictionary<string, string[]> { ["client-a"] = ["ready"] },
                new Dictionary<string, string>(),
                "passed",
                null);

            await ScenarioArtifactWriter.WriteV2Async(root, bundle);
            ScenarioReplayV2 replay = await ScenarioArtifactWriter.ReadReplayV2Async(root);
            ScenarioActionRecordV2[] replayActions =
                await ScenarioArtifactWriter.ReadActionsV2Async(root);

            string[] expectedFiles = [
                "replay-v2.json",
                "topology.json",
                "capabilities-client-a.json",
                "actions.json",
                "events-client-a.json",
                "snapshot-client-a-final.v2.json",
                "assertions.json"
            ];
            foreach (string file in expectedFiles) {
                string path = Path.Combine(root, file);
                Assert.True(File.Exists(path), path);
                byte[] bytes = await File.ReadAllBytesAsync(path);
                Assert.NotEmpty(bytes);
                Assert.DoesNotContain((byte)0, bytes);
                using JsonDocument document = JsonDocument.Parse(bytes);
                Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
            }
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
            Assert.Equal("creative-fly", replay.Scenario);
            Assert.Equal("direct", replay.Transport);
            Assert.True(Assert.Single(replayActions)
                .Payload.Deserialize<SetCreativeFlyCommand>(ControlProtocol.JsonOptions)?.Enabled);
        }
        finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainInteractionReplayParserPreservesTypedActionPayloads() {
        PlayerUseCommand use = new(7, new TerrainCellCoordinate(10, 65, 11));
        PlayerPlaceCommand place = new(7, new TerrainCellCoordinate(12, 66, 13));
        PlayerDigCommand dig = new(7, new TerrainCellCoordinate(14, 67, 15), HoldFrames: 321);
        ScenarioActionRecordV2[] records = [
            TerrainActionRecord("use", ControlCommandNames.PlayerUse, use),
            TerrainActionRecord("place", ControlCommandNames.PlayerPlace, place),
            TerrainActionRecord("dig", ControlCommandNames.PlayerDig, dig)
        ];

        TerrainInteractionReplayPlan replay =
            TerrainInteractionMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(use, replay.Use);
        Assert.Equal(place, replay.Place);
        Assert.Equal(dig, replay.Dig);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainInteractionReplayParserRejectsWrongRoleAndOrder() {
        ScenarioActionRecordV2[] records = [
            TerrainActionRecord(
                "use",
                ControlCommandNames.PlayerUse,
                new PlayerUseCommand(1, new TerrainCellCoordinate(1, 64, 1))),
            TerrainActionRecord(
                "place",
                ControlCommandNames.PlayerPlace,
                new PlayerPlaceCommand(1, new TerrainCellCoordinate(2, 64, 2))),
            TerrainActionRecord(
                "dig",
                ControlCommandNames.PlayerDig,
                new PlayerDigCommand(1, new TerrainCellCoordinate(3, 64, 3)))
        ];
        ScenarioActionRecordV2[] wrongRole = [
            records[0] with { Role = "server" },
            records[1],
            records[2]
        ];
        ScenarioActionRecordV2[] wrongOrder = [
            records[1],
            records[0],
            records[2]
        ];

        Assert.Throws<InvalidDataException>(() =>
            TerrainInteractionMultiplayerScenario.ParseReplayActions(wrongRole));
        Assert.Throws<InvalidDataException>(() =>
            TerrainInteractionMultiplayerScenario.ParseReplayActions(wrongOrder));
    }

    private static ScenarioActionRecordV2 TerrainActionRecord<TCommand>(
        string actionId,
        string command,
        TCommand payload) =>
        new(
            "client-a",
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            new ActionReceipt(actionId, true, 1, 1, "applied"));
}
