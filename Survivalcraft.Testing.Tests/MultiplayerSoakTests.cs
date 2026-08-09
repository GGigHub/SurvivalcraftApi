using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Soak;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class MultiplayerSoakTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void ScheduleIsStableAndExercisesEveryRequestedScenario() {
        MultiplayerSoakSchedule first = MultiplayerSoakSchedule.Generate(
            730,
            MultiplayerSoakSchedule.DefaultScenarioRotation.Length,
            MultiplayerTransportKind.Direct);
        MultiplayerSoakSchedule second = MultiplayerSoakSchedule.Generate(
            730,
            MultiplayerSoakSchedule.DefaultScenarioRotation.Length,
            MultiplayerTransportKind.Direct);

        Assert.Equal(first.Seed, second.Seed);
        Assert.Equal(first.Transport, second.Transport);
        AssertSchedulesEquivalent(first, second);
        Assert.Equal(
            MultiplayerSoakSchedule.DefaultScenarioRotation,
            first.Entries.Select(entry => entry.Scenario));
        Assert.All(first.Entries, entry => {
            Assert.Equal(1, entry.NetworkFaults.DuplicationRate);
            Assert.True(entry.NetworkFaults.ReorderRate > 0);
            Assert.Equal(0, entry.NetworkFaults.LossRate);
            string[] expectedTargets = entry.Scenario switch {
                CreativeFlyMultiplayerScenario.ScenarioName =>
                    [CreativeFlyMultiplayerScenario.FaultPacketType],
                SurvivalStateMultiplayerScenario.ScenarioName =>
                    SurvivalStateMultiplayerScenario.CreateFaultPacketTypes(),
                _ => []
            };
            Assert.Equal(expectedTargets, entry.NetworkFaults.TargetPacketTypes ?? []);
            _ = entry.NetworkFaults.Validate();
        });
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DifferentScheduleSeedsProduceDifferentProfiles() {
        MultiplayerSoakSchedule first = MultiplayerSoakSchedule.Generate(
            1,
            2,
            MultiplayerTransportKind.Direct);
        MultiplayerSoakSchedule second = MultiplayerSoakSchedule.Generate(
            2,
            2,
            MultiplayerTransportKind.Direct);

        Assert.NotEqual(first.Entries[0], second.Entries[0]);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LossToggleDoesNotPerturbScenarioOrOtherFaultRandomness() {
        MultiplayerSoakSchedule reliable = MultiplayerSoakSchedule.Generate(
            91,
            4,
            MultiplayerTransportKind.Direct,
            includeLoss: false);
        MultiplayerSoakSchedule lossy = MultiplayerSoakSchedule.Generate(
            91,
            4,
            MultiplayerTransportKind.Direct,
            includeLoss: true);

        for (int index = 0; index < reliable.Entries.Length; index++) {
            MultiplayerSoakScheduleEntry expected = reliable.Entries[index];
            MultiplayerSoakScheduleEntry actual = lossy.Entries[index];
            Assert.Equal(expected.Iteration, actual.Iteration);
            Assert.Equal(expected.Scenario, actual.Scenario);
            Assert.Equal(expected.ScenarioSeed, actual.ScenarioSeed);
            Assert.Equal(
                expected.NetworkFaults with { TargetPacketTypes = null },
                actual.NetworkFaults with {
                    LossRate = 0,
                    TargetPacketTypes = null,
                    GuaranteeReliableLoss = false
                });
            Assert.Equal(
                expected.NetworkFaults.TargetPacketTypes ?? [],
                actual.NetworkFaults.TargetPacketTypes ?? []);
            Assert.True(actual.NetworkFaults.LossRate > 0);
            Assert.True(actual.NetworkFaults.GuaranteeReliableLoss);
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task ScheduleRoundTripsAsReplayableJson() {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sc-soak-schedule-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "fault-schedule.json");
        try {
            MultiplayerSoakSchedule expected = MultiplayerSoakSchedule.Generate(
                42,
                3,
                MultiplayerTransportKind.Relay,
                includeLoss: true);

            await expected.WriteAsync(path);
            MultiplayerSoakSchedule actual = await MultiplayerSoakSchedule.ReadAsync(path);

            Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
            Assert.Equal(expected.Seed, actual.Seed);
            Assert.Equal(expected.Transport, actual.Transport);
            Assert.True(actual.IncludesLoss);
            AssertSchedulesEquivalent(expected, actual);
        }
        finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ResourceBudgetRejectsMissingAndOverBudgetMetrics() {
        SoakResourceBudget budget = new(
            MaximumPeakWorkingSetBytes: 100,
            MaximumPeakPrivateBytes: 200,
            MaximumWorkingSetGrowthBytes: 50,
            MaximumPeakHandles: 10,
            MaximumPeakThreads: 8,
            MinimumSamples: 2);

        SoakBudgetViolation missing = Assert.Single(
            SoakResourceBudgetEvaluator.Evaluate(
                new Dictionary<string, string>(),
                budget));
        Assert.Equal("samples", missing.Metric);

        Dictionary<string, string> metrics = [];
        ScenarioResourceMetrics.Add(
            metrics,
            "server",
            new ProcessResourceSummary(
                3,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                100,
                175,
                175,
                100,
                150,
                150,
                11,
                7,
                0,
                10));

        SoakBudgetViolation[] violations =
            SoakResourceBudgetEvaluator.Evaluate(metrics, budget);
        Assert.Contains(violations, violation =>
            violation.Metric == "peakWorkingSetBytes" && violation.Actual == 175);
        Assert.Contains(violations, violation =>
            violation.Metric == "workingSetGrowthBytes" && violation.Actual == 75);
        Assert.Contains(violations, violation =>
            violation.Metric == "peakHandles" && violation.Actual == 11);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FaultEvidenceRejectsAnUnboundedPendingQueue() {
        MultiplayerSoakScheduleEntry entry = new(
            0,
            TerrainInteractionMultiplayerScenario.ScenarioName,
            1,
            "bandwidth-reorder",
            new NetworkFaultProfile(DuplicationRate: 1, ReorderRate: 0.5));
        SoakNodeFaultEvidence[] evidence = [
            new(
                "server",
                3,
                100,
                0,
                50,
                10_001,
                "VitalStatSyncPacket",
                40_000,
                960_000)
        ];

        string[] failures = MultiplayerSoakRunner.ValidateFaultEvidence(
            entry,
            evidence,
            maximumPendingFaultPackets: 10_000);

        Assert.Contains(failures, failure =>
            failure.Contains("queued 10001 fault packets", StringComparison.Ordinal));
        Assert.Contains(failures, failure =>
            failure.Contains(
                "VitalStatSyncPacket (40000 transmissions, 960000 bytes)",
                StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LossEvidenceRequiresReliableRetryTimeoutFreedomAndFinalDrain() {
        MultiplayerSoakScheduleEntry entry = new(
            0,
            TerrainInteractionMultiplayerScenario.ScenarioName,
            1,
            "loss-latency-reorder",
            new NetworkFaultProfile(
                LossRate: 0.01,
                DuplicationRate: 1,
                ReorderRate: 0.5));
        SoakNodeFaultEvidence valid = new(
            Role: "server",
            SnapshotCount: 3,
            MaximumSent: 100,
            MaximumDropped: 1,
            MaximumDuplicated: 10,
            PeakPending: 4,
            MaximumReliableCreated: 12,
            MaximumReliableRetried: 2,
            MaximumReliableAcknowledged: 12,
            MaximumReliableTimedOut: 0,
            PeakReliablePending: 3,
            PeakReliablePendingBytes: 1024,
            FinalSnapshotCount: 1,
            MaximumFinalReliablePending: 0,
            MaximumFinalReliablePendingBytes: 0);

        Assert.Empty(MultiplayerSoakRunner.ValidateFaultEvidence(
            entry,
            [valid],
            maximumPendingFaultPackets: 100));

        string[] failures = MultiplayerSoakRunner.ValidateFaultEvidence(
            entry,
            [valid with {
                MaximumReliableRetried = 0,
                MaximumReliableTimedOut = 1,
                MaximumReliableFailed = 1,
                MaximumFinalReliablePending = 1,
                MaximumFinalReliablePendingBytes = 64
            }],
            maximumPendingFaultPackets: 100);
        Assert.Contains(failures, failure => failure.Contains(
            "no application-reliable retry",
            StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains(
            "timed-out application-reliable deliveries",
            StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains(
            "failed application-reliable deliveries",
            StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains(
            "ended with 1 pending application-reliable deliveries",
            StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void OnlyRoleTerminalSnapshotCountsAsFinalReliabilityEvidence() {
        Assert.True(MultiplayerSoakRunner.IsTerminalReliabilitySnapshotPath(
            @"C:\artifacts\snapshot-client-a-final.v2.json",
            "client-a"));
        Assert.False(MultiplayerSoakRunner.IsTerminalReliabilitySnapshotPath(
            @"C:\artifacts\snapshot-client-a-place-final.v2.json",
            "client-a"));
        Assert.False(MultiplayerSoakRunner.IsTerminalReliabilitySnapshotPath(
            @"C:\artifacts\snapshot-client-b-final.v2.json",
            "client-a"));
    }

    private static void AssertSchedulesEquivalent(
        MultiplayerSoakSchedule expected,
        MultiplayerSoakSchedule actual) {
        Assert.Equal(expected.Entries.Length, actual.Entries.Length);
        for (int index = 0; index < expected.Entries.Length; index++) {
            MultiplayerSoakScheduleEntry expectedEntry = expected.Entries[index];
            MultiplayerSoakScheduleEntry actualEntry = actual.Entries[index];
            Assert.Equal(expectedEntry.Iteration, actualEntry.Iteration);
            Assert.Equal(expectedEntry.Scenario, actualEntry.Scenario);
            Assert.Equal(expectedEntry.ScenarioSeed, actualEntry.ScenarioSeed);
            Assert.Equal(expectedEntry.FaultClass, actualEntry.FaultClass);
            Assert.Equal(
                expectedEntry.NetworkFaults with { TargetPacketTypes = null },
                actualEntry.NetworkFaults with { TargetPacketTypes = null });
            Assert.Equal(
                expectedEntry.NetworkFaults.TargetPacketTypes ?? [],
                actualEntry.NetworkFaults.TargetPacketTypes ?? []);
        }
    }
}
