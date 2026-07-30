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
            const string productionEntry = "ComponentGui.TrySetCreativeFlyEnabled";
            ActionReceipt receipt = new(
                "a1",
                true,
                1,
                1,
                "applied",
                Provenance: ActionProvenance.ProductionInput,
                ProductionEntry: productionEntry);
            NetworkFaultProfile faultProfile = new(
                Seed: 42,
                BaseLatencyMilliseconds: 18,
                DuplicationRate: 1,
                ReorderRate: 0.5,
                ReorderDelayMilliseconds: 18);
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
                new ScenarioReplayV2(
                    "creative-fly",
                    1,
                    42,
                    "runtime",
                    "direct",
                    1,
                    2,
                    2,
                    faultProfile),
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
                    ["client-a"] = new ActionEventsResult([
                        new ActionEventRecord(
                            1,
                            DateTimeOffset.UnixEpoch,
                            1,
                            "a1",
                            "client-a",
                            AutomationCommandDomain.Player,
                            "creativeFly",
                            ActionEventPhase.Queued,
                            1,
                            null,
                            "queued",
                            new Dictionary<string, string>(),
                            ActionProvenance.ProductionInput,
                            productionEntry),
                        new ActionEventRecord(
                            2,
                            DateTimeOffset.UnixEpoch,
                            2,
                            "a1",
                            "client-a",
                            AutomationCommandDomain.Player,
                            "creativeFly",
                            ActionEventPhase.Applied,
                            1,
                            null,
                            "applied",
                            new Dictionary<string, string>(),
                            ActionProvenance.ProductionInput,
                            productionEntry)
                    ], 1, 2, 0, false)
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
                "attestations.json",
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
            Assert.Equal(faultProfile, replay.NetworkFaults);
            Assert.True(Assert.Single(replayActions)
                .Payload.Deserialize<SetCreativeFlyCommand>(ControlProtocol.JsonOptions)?.Enabled);
            ProductionPathAttestation[] attestations = JsonSerializer.Deserialize<
                ProductionPathAttestation[]>(
                await File.ReadAllTextAsync(Path.Combine(root, "attestations.json")),
                ControlProtocol.JsonOptions) ?? [];
            Assert.True(Assert.Single(attestations).Passed);
        }
        finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ProductionPathAttestationRejectsFixtureAndMismatchedEventEvidence() {
        const string actionId = "action-1";
        const string productionEntry = "MoveItemPacket.Handle";
        ScenarioActionRecordV2 action = new(
            "client-a",
            ControlCommandNames.InventoryMove,
            JsonSerializer.SerializeToElement(
                new InventoryMoveCommand(1, 10, 0, 20, 0, 1),
                ControlProtocol.JsonOptions),
            new ActionReceipt(
                actionId,
                true,
                1,
                1,
                "queued",
                Provenance: ActionProvenance.ProductionRequest,
                ProductionEntry: productionEntry));
        ActionEventRecord queued = new(
            1,
            DateTimeOffset.UnixEpoch,
            1,
            actionId,
            "client-a",
            AutomationCommandDomain.Inventory,
            "move",
            ActionEventPhase.Queued,
            1,
            null,
            "queued",
            new Dictionary<string, string>(),
            ActionProvenance.ProductionRequest,
            productionEntry);
        ActionEventRecord mismatchedTerminal = queued with {
            Sequence = 2,
            Phase = ActionEventPhase.Applied,
            ProductionEntry = "Fixture.ApplyInventory"
        };

        ProductionPathAttestation attestation = Assert.Single(
            ProductionPathAttestationBuilder.Build(
                [action],
                new Dictionary<string, ActionEventsResult> {
                    ["client-a"] = new ActionEventsResult(
                        [queued, mismatchedTerminal],
                        1,
                        2,
                        0,
                        false)
                }));

        Assert.False(attestation.Passed);
        Assert.Contains(
            "provenance-stamped applied/observed/rejected event",
            attestation.MissingEvidence);
        Assert.Throws<InvalidDataException>(() =>
            ProductionPathAttestationBuilder.EnsureComplete([attestation]));

        ProductionPathAttestation fixture = Assert.Single(
            ProductionPathAttestationBuilder.Build(
                [action with {
                    Receipt = action.Receipt with {
                        Provenance = ActionProvenance.Fixture,
                        ProductionEntry = "Fixture.ApplyInventory"
                    }
                }],
                new Dictionary<string, ActionEventsResult>()));
        Assert.False(fixture.Passed);
        Assert.Contains("non-fixture receipt provenance", fixture.MissingEvidence);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task PassedV2BundleFailsClosedWhenProductionEvidenceIsMissing() {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.Testing.Tests",
            Guid.NewGuid().ToString("N"));
        try {
            ScenarioArtifactBundleV2 bundle = new(
                new ScenarioDefinition("gate", 1, 1, ["client-a"], []),
                new ScenarioReplayV2(
                    "gate",
                    1,
                    1,
                    "runtime",
                    "direct",
                    ControlProtocol.CurrentVersion,
                    AutomationProtocol.CurrentApiVersion,
                    AutomationProtocol.CurrentSnapshotSchemaVersion),
                new ScenarioTopologyV2("direct", "server", ["client-a"], false, null),
                [],
                new Dictionary<string, AutomationDescription>(),
                [new ScenarioActionRecordV2(
                    "client-a",
                    ControlCommandNames.SetCreativeFly,
                    JsonSerializer.SerializeToElement(
                        new SetCreativeFlyCommand(true, 1),
                        ControlProtocol.JsonOptions),
                    new ActionReceipt("missing", true, 1, 1, "queued"))],
                new Dictionary<string, ActionEventsResult>(),
                new Dictionary<string, StateSnapshotV2>(),
                [],
                new Dictionary<string, string[]>(),
                new Dictionary<string, string>(),
                "passed",
                null);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                ScenarioArtifactWriter.WriteV2Async(root, bundle));

            Assert.Contains("Production path attestation failed", exception.Message);
            Assert.True(File.Exists(Path.Combine(root, "attestations.json")));
            using JsonDocument outcome = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(root, "outcome.json")));
            Assert.Equal("failed", outcome.RootElement.GetProperty("outcome").GetString());
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

    [Fact]
    [Trait("Layer", "L0")]
    public void PlayerMotionReplayParserPreservesStrictTypedActionSequence() {
        PlayerMoveCommand move = new(7, new Vector3State(0, 0, 0.75), HoldFrames: 45);
        PlayerLookCommand look = new(7, new Vector2State(0.6, -0.35), HoldFrames: 10);
        PlayerSetCrouchCommand crouch = new(7, true);
        PlayerLookCommand persistenceLook = new(
            7,
            new Vector2State(-0.45, 0.25),
            HoldFrames: 10);
        PlayerSetCrouchCommand persistenceCrouch = new(7, true);
        ScenarioActionRecordV2[] records = [
            TerrainActionRecord("move", ControlCommandNames.PlayerMove, move),
            TerrainActionRecord("look", ControlCommandNames.PlayerLook, look),
            TerrainActionRecord("crouch", ControlCommandNames.PlayerSetCrouch, crouch),
            TerrainActionRecord(
                "persistence-look",
                ControlCommandNames.PlayerLook,
                persistenceLook),
            TerrainActionRecord(
                "persistence-crouch",
                ControlCommandNames.PlayerSetCrouch,
                persistenceCrouch)
        ];

        PlayerMotionReplayPlan replay =
            PlayerMotionMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(move, replay.InitialMove);
        Assert.Equal(look, replay.InitialLook);
        Assert.Equal(crouch, replay.InitialCrouch);
        Assert.Equal(persistenceLook, replay.PersistenceLook);
        Assert.Equal(persistenceCrouch, replay.PersistenceCrouch);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainStreamingReplayParserPreservesMoveAndRevisionBurst() {
        PlayerMoveCommand move = new(7, new Vector3State(0, 0, 0.75), HoldFrames: 45);
        RunTerrainRevisionBurstCommand burst = new(
            new TerrainCellCoordinate(24, 70, -8),
            [2, 3, 4],
            SendStaleSnapshot: true);
        ScenarioActionRecordV2[] records = [
            TerrainActionRecord("move", ControlCommandNames.PlayerMove, move),
            new ScenarioActionRecordV2(
                "server",
                ControlCommandNames.RunTerrainRevisionBurst,
                JsonSerializer.SerializeToElement(burst, ControlProtocol.JsonOptions),
                new ActionReceipt("burst", true, 2, -1, "stale_snapshot_queued"))
        ];

        TerrainStreamingReplayPlan replay =
            TerrainStreamingMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(move, replay.Move);
        Assert.Equal(burst.Cell, replay.RevisionBurst.Cell);
        Assert.Equal(burst.Values, replay.RevisionBurst.Values);
        Assert.Equal(burst.SendStaleSnapshot, replay.RevisionBurst.SendStaleSnapshot);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainStreamingReplayParserRejectsWrongOrderAndMissingStaleProbe() {
        PlayerMoveCommand move = new(1, new Vector3State(0, 0, 0.75));
        RunTerrainRevisionBurstCommand burst = new(
            new TerrainCellCoordinate(1, 64, 1),
            [2, 3],
            SendStaleSnapshot: true);
        ScenarioActionRecordV2 moveRecord =
            TerrainActionRecord("move", ControlCommandNames.PlayerMove, move);
        ScenarioActionRecordV2 burstRecord = new(
            "server",
            ControlCommandNames.RunTerrainRevisionBurst,
            JsonSerializer.SerializeToElement(burst, ControlProtocol.JsonOptions),
            new ActionReceipt("burst", true, 2, -1, "stale_snapshot_queued"));

        Assert.Throws<InvalidDataException>(() =>
            TerrainStreamingMultiplayerScenario.ParseReplayActions([burstRecord, moveRecord]));
        Assert.Throws<InvalidDataException>(() =>
            TerrainStreamingMultiplayerScenario.ParseReplayActions([
                moveRecord,
                burstRecord with {
                    Payload = JsonSerializer.SerializeToElement(
                        burst with { SendStaleSnapshot = false },
                        ControlProtocol.JsonOptions)
                }
            ]));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EntityCreatureReplayParserPreservesStrictServerActionSequence() {
        ApplyEntityImpulseCommand impulse = new(
            "survivor",
            new Vector3State(1.5, 0, 0.5),
            ActiveFrames: 12);
        RunEntityLifecycleBurstCommand burst = new(
            "replacement",
            "tombstone",
            SendStaleLoads: true);
        ScenarioActionRecordV2[] records = [
            ServerActionRecord("impulse", ControlCommandNames.ApplyEntityImpulse, impulse),
            ServerActionRecord("burst", ControlCommandNames.RunEntityLifecycleBurst, burst)
        ];

        EntityCreatureReplayPlan replay =
            EntityCreatureMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(impulse, replay.Impulse);
        Assert.Equal(burst, replay.LifecycleBurst);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EntityCreatureReplayParserRejectsWrongOrderRoleAndMissingStaleProbe() {
        ScenarioActionRecordV2 impulse = ServerActionRecord(
            "impulse",
            ControlCommandNames.ApplyEntityImpulse,
            new ApplyEntityImpulseCommand("survivor", new Vector3State(1, 0, 0)));
        ScenarioActionRecordV2 burst = ServerActionRecord(
            "burst",
            ControlCommandNames.RunEntityLifecycleBurst,
            new RunEntityLifecycleBurstCommand());

        Assert.Throws<InvalidDataException>(() =>
            EntityCreatureMultiplayerScenario.ParseReplayActions([burst, impulse]));
        Assert.Throws<InvalidDataException>(() =>
            EntityCreatureMultiplayerScenario.ParseReplayActions([
                impulse with { Role = "client-a" },
                burst
            ]));
        Assert.Throws<InvalidDataException>(() =>
            EntityCreatureMultiplayerScenario.ParseReplayActions([
                impulse,
                burst with {
                    Payload = JsonSerializer.SerializeToElement(
                        new RunEntityLifecycleBurstCommand(SendStaleLoads: false),
                        ControlProtocol.JsonOptions)
                }
            ]));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PlayerMotionReplayParserRejectsWrongRoleOrderAndTransientNoOp() {
        ScenarioActionRecordV2[] records = [
            TerrainActionRecord(
                "move",
                ControlCommandNames.PlayerMove,
                new PlayerMoveCommand(1, new Vector3State(0, 0, 0.75))),
            TerrainActionRecord(
                "look",
                ControlCommandNames.PlayerLook,
                new PlayerLookCommand(1, new Vector2State(0.5, -0.25))),
            TerrainActionRecord(
                "crouch",
                ControlCommandNames.PlayerSetCrouch,
                new PlayerSetCrouchCommand(1, true)),
            TerrainActionRecord(
                "persistence-look",
                ControlCommandNames.PlayerLook,
                new PlayerLookCommand(1, new Vector2State(-0.5, 0.25))),
            TerrainActionRecord(
                "persistence-crouch",
                ControlCommandNames.PlayerSetCrouch,
                new PlayerSetCrouchCommand(1, true))
        ];

        Assert.Throws<InvalidDataException>(() =>
            PlayerMotionMultiplayerScenario.ParseReplayActions([
                records[0] with { Role = "server" },
                .. records[1..]
            ]));
        Assert.Throws<InvalidDataException>(() =>
            PlayerMotionMultiplayerScenario.ParseReplayActions([
                records[1],
                records[0],
                .. records[2..]
            ]));
        Assert.Throws<InvalidDataException>(() =>
            PlayerMotionMultiplayerScenario.ParseReplayActions([
                .. records[..2],
                records[2] with {
                    Payload = JsonSerializer.SerializeToElement(
                        new PlayerSetCrouchCommand(1, false),
                        ControlProtocol.JsonOptions)
                },
                .. records[3..]
            ]));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void InventoryHealthReplayParserPreservesStrictCrossDomainSequence() {
        ScenarioActionRecordV2[] records = [
            TerrainActionRecord(
                "move",
                ControlCommandNames.InventoryMove,
                new InventoryMoveCommand(1, 10, 0, 20, 0, 3)),
            TerrainActionRecord(
                "select",
                ControlCommandNames.InventorySelectSlot,
                new InventorySelectSlotCommand(1, 10, 1)),
            TerrainActionRecord(
                "drop",
                ControlCommandNames.InventoryDrop,
                new InventoryDropCommand(1)),
            ServerActionRecord(
                "inventory-burst",
                ControlCommandNames.RunInventoryRevisionBurst,
                new RunInventoryRevisionBurstCommand(20, 0)),
            TerrainActionRecord(
                "hit",
                ControlCommandNames.PlayerHit,
                new PlayerHitCommand(1, 30)),
            ServerActionRecord(
                "health-burst",
                ControlCommandNames.RunHealthRevisionBurst,
                new RunHealthRevisionBurstCommand(30)),
            TerrainActionRecord(
                "respawn",
                ControlCommandNames.PlayerRequestRespawn,
                new PlayerRequestRespawnCommand(1))
        ];

        InventoryHealthReplayPlan plan =
            InventoryHealthMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(3, plan.Move.Count);
        Assert.Equal(1, plan.SelectSlot.SlotIndex);
        Assert.Equal(20, plan.InventoryRevisionBurst.InventoryId);
        Assert.Equal(30, plan.Hit.TargetEntityId);
        Assert.Equal(30, plan.HealthRevisionBurst.EntityId);
        Assert.Equal(1, plan.Respawn.PlayerIndex);
        Assert.Throws<InvalidDataException>(() =>
            InventoryHealthMultiplayerScenario.ParseReplayActions([
                records[0],
                records[2],
                records[1],
                .. records[3..]
            ]));
        Assert.Throws<InvalidDataException>(() =>
            InventoryHealthMultiplayerScenario.ParseReplayActions([
                .. records[..5],
                records[5] with {
                    Payload = JsonSerializer.SerializeToElement(
                        new RunHealthRevisionBurstCommand(30, SendStaleSnapshot: false),
                        ControlProtocol.JsonOptions)
                },
                records[6]
            ]));
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

    private static ScenarioActionRecordV2 ServerActionRecord<TCommand>(
        string actionId,
        string command,
        TCommand payload) =>
        new(
            "server",
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            new ActionReceipt(actionId, true, 1, -1, "applied"));
}
