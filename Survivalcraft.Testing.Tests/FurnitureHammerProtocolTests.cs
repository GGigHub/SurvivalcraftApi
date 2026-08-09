using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class FurnitureHammerProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void AutoTestV12FurnitureHammerCommandsRoundTripWithAuthorityFields() {
        FurnitureHammerFixture fixture = new(
            3,
            41,
            51,
            FurnitureHammerFixtureMode.Valid,
            new TerrainCellCoordinate(6, 70, 13),
            new Vector3State(6.5, 71, 16),
            21,
            21,
            230,
            2,
            7);
        PrepareFurnitureHammerFixtureCommand prepare = new(
            3,
            FurnitureHammerFixtureMode.OutOfRange,
            fixture.SourceCell);
        ApplyFurnitureHammerFixtureCommand apply = new(fixture);
        FurnitureHammerCommand action = new(
            3,
            fixture,
            FurnitureHammerExpectedResult.DesignMismatch,
            ForgeDesign: true,
            ProbeRateLimit: false,
            DuplicateCount: 3);

        Assert.Equal(14, AutomationProtocol.CurrentApiVersion);
        Assert.Equal(
            prepare,
            ControlProtocol.CreateRequest(
                    ControlCommandNames.PrepareFurnitureHammerFixture,
                    prepare)
                .DeserializePayload<PrepareFurnitureHammerFixtureCommand>());
        Assert.Equal(
            apply,
            ControlProtocol.CreateRequest(
                    ControlCommandNames.ApplyFurnitureHammerFixture,
                    apply)
                .DeserializePayload<ApplyFurnitureHammerFixtureCommand>());
        Assert.Equal(
            action,
            ControlProtocol.CreateRequest(ControlCommandNames.FurnitureHammer, action)
                .DeserializePayload<FurnitureHammerCommand>());
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FurnitureDesignSnapshotsCanonicalizeAndCompareByStableIndex() {
        StateSnapshotV2 expected = Snapshot() with {
            FurnitureDesigns = [
                new FurnitureDesignSnapshotV2(9, 4, "chair", "None", 2, "B"),
                new FurnitureDesignSnapshotV2(3, 2, "table", "None", 1, "A")
            ]
        };
        StateSnapshotV2 actual = Snapshot() with {
            FurnitureDesigns = [
                new FurnitureDesignSnapshotV2(3, 2, "table", "None", 1, "A"),
                new FurnitureDesignSnapshotV2(9, 4, "chair", "None", 2, "B")
            ]
        };

        StateSnapshotV2 normalized = SnapshotV2Canonicalizer.Normalize(expected);
        Assert.Equal([3, 9], normalized.FurnitureDesigns!.Select(item => item.DesignIndex));
        Assert.True(SnapshotV2Comparer.CompareFurnitureDesigns(expected, actual).Equal);

        SnapshotV2Diff mismatch = SnapshotV2Comparer.CompareFurnitureDesigns(
            expected,
            actual with {
                FurnitureDesigns = [
                    actual.FurnitureDesigns![0],
                    actual.FurnitureDesigns[1] with { ValueFingerprint = "changed" }
                ]
            });
        Assert.False(mismatch.Equal);
        Assert.Contains(mismatch.Differences, difference =>
            difference.Path == "$.furnitureDesigns[designIndex=9].valueFingerprint");
    }

    private static StateSnapshotV2 Snapshot() => new(
        AutomationProtocol.CurrentSnapshotSchemaVersion,
        "capture",
        12,
        1.25,
        new NodeSnapshotV2("client-a", "GameScreen", true, true, "network"),
        null,
        [],
        [],
        [],
        new Dictionary<string, string>());
}
