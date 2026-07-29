using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class SnapshotV2Tests {
    [Fact]
    [Trait("Layer", "L0")]
    public void CanonicalHashUsesStablePlayerAndEventOrdering() {
        ActionEventRecord firstEvent = new(
            1,
            DateTimeOffset.UnixEpoch,
            10,
            "a1",
            "client-a",
            AutomationCommandDomain.Player,
            "creativeFly",
            ActionEventPhase.Applied,
            1,
            null,
            "applied",
            new Dictionary<string, string> { ["z"] = "2", ["a"] = "1" });
        ActionEventRecord secondEvent = firstEvent with { Sequence = 2, ActionId = "a2" };
        PlayerSnapshotV2 firstPlayer = Player(1, "A", 1.0000001);
        PlayerSnapshotV2 secondPlayer = Player(2, "B", 2);
        StateSnapshotV2 first = Snapshot(
            [secondPlayer, firstPlayer],
            [secondEvent, firstEvent]);
        StateSnapshotV2 second = Snapshot(
            [firstPlayer, secondPlayer],
            [firstEvent, secondEvent]);

        Assert.Equal(
            SnapshotV2Canonicalizer.ComputeSha256(first),
            SnapshotV2Canonicalizer.ComputeSha256(second));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SnapshotV2ProjectionCommandRoundTripsThroughWireV1() {
        ControlRequest request = ControlProtocol.CreateRequest(
            ControlCommandNames.GetSnapshotV2,
            new GetSnapshotV2Command(
                [SnapshotDomain.Network, SnapshotDomain.Players, SnapshotDomain.Terrain, SnapshotDomain.Entities, SnapshotDomain.Events],
                PlayerIndexes: [1, 2],
                TerrainCells: [
                    new TerrainCellCoordinate(10, 64, 20),
                    new TerrainCellCoordinate(11, 64, 20)
                ],
                EventAfterSequence: 7,
                EventLimit: 64,
                EntityIds: [31, 32]));

        GetSnapshotV2Command command = request.DeserializePayload<GetSnapshotV2Command>();

        Assert.Equal(ControlProtocol.CurrentVersion, request.Version);
        Assert.Equal(
            new[] {
                SnapshotDomain.Network,
                SnapshotDomain.Players,
                SnapshotDomain.Terrain,
                SnapshotDomain.Entities,
                SnapshotDomain.Events
            },
            command.Domains);
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(command.PlayerIndexes));
        Assert.Equal(
            new[] {
                new TerrainCellCoordinate(10, 64, 20),
                new TerrainCellCoordinate(11, 64, 20)
            },
            Assert.IsType<TerrainCellCoordinate[]>(command.TerrainCells));
        Assert.Equal(7, command.EventAfterSequence);
        Assert.Equal(64, command.EventLimit);
        Assert.Equal(new[] { 31, 32 }, Assert.IsType<int[]>(command.EntityIds));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CanonicalizerRejectsWrongSchemaBeforeComparison() {
        StateSnapshotV2 snapshot = Snapshot([], []) with { SchemaVersion = 1 };

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SnapshotV2Canonicalizer.Normalize(snapshot));

        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PlayerComparerUsesStableKeysAndVectorTolerance() {
        StateSnapshotV2 expected = Snapshot(
            [Player(2, "B", 2), Player(1, "A", 1)],
            []);
        StateSnapshotV2 actual = Snapshot(
            [Player(1, "A", 1.02), Player(2, "B", 2.01)],
            []);

        SnapshotV2Diff equal = SnapshotV2Comparer.ComparePlayers(
            expected,
            actual,
            new SnapshotV2PlayerComparisonOptions(PositionTolerance: 0.03));
        SnapshotV2Diff different = SnapshotV2Comparer.ComparePlayers(
            expected,
            actual with {
                Players = actual.Players.Select(player =>
                    player.PlayerIndex == 1
                        ? player with { CreativeFlyEnabled = true }
                        : player).ToArray()
            },
            new SnapshotV2PlayerComparisonOptions(PositionTolerance: 0.03));

        Assert.True(equal.Equal);
        SnapshotDifference difference = Assert.Single(different.Differences);
        Assert.Equal("$.players[playerIndex=1].creativeFlyEnabled", difference.Path);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PlayerComparerAndCanonicalizerIncludeLookAngles() {
        PlayerSnapshotV2 player = Player(1, "A", 1) with {
            Body = Player(1, "A", 1).Body! with {
                LookAngles = new Vector2State(0.1234567, -0.25),
                Rotation = new QuaternionState(0, 0, 0, 1)
            }
        };
        StateSnapshotV2 expected = Snapshot([player], []);
        StateSnapshotV2 withinTolerance = expected with {
            Players = [player with {
                Body = player.Body! with {
                    LookAngles = new Vector2State(0.133, -0.25),
                    Rotation = new QuaternionState(0, 0, 0, -1)
                }
            }]
        };
        StateSnapshotV2 outsideTolerance = expected with {
            Players = [player with {
                Body = player.Body! with {
                    LookAngles = new Vector2State(0.16, -0.25),
                    Rotation = new QuaternionState(0, Math.Sin(0.05), 0, Math.Cos(0.05))
                }
            }]
        };
        StateSnapshotV2 equivalentQuaternion = expected with {
            Players = [player with {
                Body = player.Body! with { Rotation = new QuaternionState(0, 0, 0, -1) }
            }]
        };

        PlayerBodySnapshotV2 normalizedBody = Assert.Single(
            SnapshotV2Canonicalizer.Normalize(expected, decimalPlaces: 5).Players).Body!;
        Assert.Equal(new Vector2State(0.12346, -0.25), normalizedBody.LookAngles);
        Assert.Equal(new QuaternionState(0, 0, 0, 1), normalizedBody.Rotation);
        Assert.Equal(
            SnapshotV2Canonicalizer.ComputeSha256(expected),
            SnapshotV2Canonicalizer.ComputeSha256(equivalentQuaternion));
        Assert.True(SnapshotV2Comparer.ComparePlayers(
            expected,
            withinTolerance,
            new SnapshotV2PlayerComparisonOptions(LookAngleTolerance: 0.02)).Equal);
        SnapshotV2Diff difference = SnapshotV2Comparer.ComparePlayers(
            expected,
            outsideTolerance,
            new SnapshotV2PlayerComparisonOptions(LookAngleTolerance: 0.02));
        Assert.Contains(
            difference.Differences,
            item => item.Path == "$.players[playerIndex=1].body.lookAngles");
        Assert.Contains(
            difference.Differences,
            item => item.Path == "$.players[playerIndex=1].body.rotation");
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainProjectionIsCanonicalAndComparedByCoordinate() {
        TerrainCellSnapshotV2 firstCell = new(10, 64, 20, 5, 5, 0, true);
        TerrainCellSnapshotV2 secondCell = new(11, 64, 20, 21, 21, 0, true);
        StateSnapshotV2 first = Snapshot([], [], [secondCell, firstCell]);
        StateSnapshotV2 second = Snapshot([], [], [firstCell, secondCell]);

        Assert.Equal(
            SnapshotV2Canonicalizer.ComputeSha256(first),
            SnapshotV2Canonicalizer.ComputeSha256(second));
        Assert.True(SnapshotV2Comparer.CompareTerrainCells(first, second).Equal);

        StateSnapshotV2 changed = second with {
            TerrainCells = [firstCell, secondCell with { Value = 0, Contents = 0 }]
        };
        SnapshotV2Diff difference = SnapshotV2Comparer.CompareTerrainCells(first, changed);

        Assert.False(difference.Equal);
        Assert.Contains(
            difference.Differences,
            item => item.Path == "$.terrainCells[x=11,y=64,z=20].value");

        StateSnapshotV2 unloaded = second with {
            TerrainCells = [firstCell, secondCell with { IsLoaded = false }]
        };
        SnapshotV2Diff loadedDifference =
            SnapshotV2Comparer.CompareTerrainCells(first, unloaded);
        Assert.Contains(
            loadedDifference.Differences,
            item => item.Path == "$.terrainCells[x=11,y=64,z=20].isLoaded");
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainRevisionComparisonIsExplicitlyOptIn() {
        TerrainCellSnapshotV2 expectedCell = new(
            10, 64, 20, 5, 5, 0, true, Revision: 7, ChunkState: "Valid");
        TerrainCellSnapshotV2 actualCell = expectedCell with { Revision = 8 };
        StateSnapshotV2 expected = Snapshot([], [], [expectedCell]);
        StateSnapshotV2 actual = Snapshot([], [], [actualCell]);

        Assert.True(SnapshotV2Comparer.CompareTerrainCells(expected, actual).Equal);
        SnapshotV2Diff revisionDifference = SnapshotV2Comparer.CompareTerrainCells(
            expected,
            actual,
            new SnapshotV2TerrainComparisonOptions(CompareRevision: true));

        SnapshotDifference difference = Assert.Single(revisionDifference.Differences);
        Assert.Equal("$.terrainCells[x=10,y=64,z=20].revision", difference.Path);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainStreamingProjectionCanonicalizesLocationsAndPreservesMetrics() {
        StateSnapshotV2 snapshot = Snapshot([], []) with {
            TerrainStreaming = new TerrainStreamingSnapshotV2(
                [
                    new TerrainUpdateLocationSnapshotV2(
                        2,
                        new Vector2State(2.1234567, 3.7654321),
                        48.1234567,
                        64.7654321,
                        3,
                        4),
                    new TerrainUpdateLocationSnapshotV2(
                        1,
                        new Vector2State(1, 2),
                        32,
                        48,
                        1,
                        2)
                ],
                10,
                9,
                4,
                6,
                0,
                10,
                11,
                12,
                13,
                14,
                15,
                16,
                17)
        };

        TerrainStreamingSnapshotV2 normalized = Assert.IsType<TerrainStreamingSnapshotV2>(
            SnapshotV2Canonicalizer.Normalize(snapshot, decimalPlaces: 5).TerrainStreaming);

        Assert.Equal(new[] { 1, 2 }, normalized.Locations.Select(item => item.PlayerIndex));
        Assert.Equal(new Vector2State(2.12346, 3.76543), normalized.Locations[1].Center);
        Assert.Equal(48.12346, normalized.Locations[1].VisibilityDistance);
        Assert.Equal(64.76543, normalized.Locations[1].ContentDistance);
        Assert.Equal(14, normalized.IgnoredUnloadedChanges);
        Assert.Equal(17, normalized.PendingRevisionChanges);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void TerrainLoadednessIsScopedToSnapshotV2() {
        string legacyJson = JsonSerializer.Serialize(
            new TerrainCellState(10, 64, 20, 0, 0, 0),
            ControlProtocol.JsonOptions);
        string v2Json = JsonSerializer.Serialize(
            new TerrainCellSnapshotV2(10, 64, 20, 0, 0, 0, false),
            ControlProtocol.JsonOptions);

        Assert.DoesNotContain("\"isLoaded\"", legacyJson, StringComparison.Ordinal);
        Assert.Contains("\"isLoaded\":false", v2Json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EntityProjectionIsCanonicalAndComparedByEntityId() {
        EntitySnapshotV2 firstEntity = Entity(31, 1.234567, revision: 3);
        EntitySnapshotV2 secondEntity = Entity(32, 2, revision: 1);
        StateSnapshotV2 first = Snapshot([], []) with {
            Entities = [secondEntity, firstEntity],
            EntityLifecycle = new EntityLifecycleSnapshotV2(2, 2, 3, 1, 4, 1)
        };
        StateSnapshotV2 second = Snapshot([], []) with {
            Entities = [
                firstEntity with {
                    Body = firstEntity.Body! with {
                        Rotation = new QuaternionState(0, 0, 0, -1)
                    }
                },
                secondEntity
            ],
            EntityLifecycle = first.EntityLifecycle
        };

        StateSnapshotV2 normalized = SnapshotV2Canonicalizer.Normalize(first, decimalPlaces: 5);
        EntitySnapshotV2[] normalizedEntities = Assert.IsType<EntitySnapshotV2[]>(
            normalized.Entities);
        Assert.Equal(new[] { 31, 32 }, normalizedEntities.Select(entity => entity.EntityId));
        Assert.Equal(1.23457, normalizedEntities[0].Body!.Position.X);
        Assert.Equal(
            SnapshotV2Canonicalizer.ComputeSha256(first),
            SnapshotV2Canonicalizer.ComputeSha256(second));
        Assert.True(SnapshotV2Comparer.CompareEntities(first, second).Equal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EntityComparerIncludesRevisionAndTombstoneState() {
        EntitySnapshotV2 expectedEntity = Entity(31, 1, revision: 3);
        StateSnapshotV2 expected = Snapshot([], []) with { Entities = [expectedEntity] };
        StateSnapshotV2 revised = expected with {
            Entities = [expectedEntity with { Revision = 4 }]
        };
        StateSnapshotV2 tombstoned = expected with {
            Entities = [expectedEntity with {
                Exists = false,
                TemplateName = null,
                IsCreature = false,
                Body = null,
                Health = null,
                Hitpoints = null,
                Revision = 4,
                IsTombstone = true
            }]
        };

        SnapshotV2Diff revisionDifference = SnapshotV2Comparer.CompareEntities(expected, revised);
        SnapshotV2Diff tombstoneDifference = SnapshotV2Comparer.CompareEntities(expected, tombstoned);

        Assert.Contains(
            revisionDifference.Differences,
            item => item.Path == "$.entities[entityId=31].revision");
        Assert.Contains(
            tombstoneDifference.Differences,
            item => item.Path == "$.entities[entityId=31].isTombstone");
        Assert.Contains(
            tombstoneDifference.Differences,
            item => item.Path == "$.entities[entityId=31].exists");
    }

    private static StateSnapshotV2 Snapshot(
        PlayerSnapshotV2[] players,
        ActionEventRecord[] events,
        TerrainCellSnapshotV2[]? terrainCells = null) =>
        new(
            AutomationProtocol.CurrentSnapshotSchemaVersion,
            "capture",
            12,
            1.234567,
            new NodeSnapshotV2("client-a", "GameScreen", true, true, "network"),
            new NetworkSnapshotV2("client", "Playing", 1, 0, "none", 0, 0, 0, 0),
            players,
            terrainCells ?? [],
            events,
            new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" });

    private static PlayerSnapshotV2 Player(int index, string name, double position) =>
        new(
            index,
            name,
            null,
            index == 1,
            true,
            index,
            new PlayerBodySnapshotV2(
                new Vector3State(position, 2, 3),
                new Vector3State(0, 0, 0),
                false),
            1,
            1,
            false);

    private static EntitySnapshotV2 Entity(int id, double position, long revision) =>
        new(
            id,
            true,
            "Cow",
            true,
            false,
            new EntityBodySnapshotV2(
                new Vector3State(position, 2, 3),
                new Vector3State(0, 0, 0),
                new QuaternionState(0, 0, 0, 1)),
            1,
            1,
            revision,
            false);
}
