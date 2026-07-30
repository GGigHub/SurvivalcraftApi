using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class ProcessResourceMetricsTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void SummaryDerivesGrowthCpuAndDuration() {
        DateTimeOffset started = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        ProcessResourceSummary summary = new(
            5,
            started,
            started.AddSeconds(2),
            100,
            175,
            200,
            300,
            410,
            450,
            12,
            8,
            25,
            90);

        Assert.Equal(75, summary.WorkingSetGrowthBytes);
        Assert.Equal(110, summary.PrivateMemoryGrowthBytes);
        Assert.Equal(65, summary.ProcessorTimeMilliseconds);
        Assert.Equal(2000, summary.SampledDurationMilliseconds);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ScenarioMetricsExposeEveryBudgetInput() {
        Dictionary<string, string> metrics = [];
        ProcessResourceSummary summary = new(
            3,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(500),
            100,
            125,
            150,
            200,
            240,
            260,
            9,
            7,
            10,
            30);

        ScenarioResourceMetrics.Add(metrics, "client-a", summary);

        Assert.Equal("3", metrics["resource.client-a.samples"]);
        Assert.Equal("150", metrics["resource.client-a.peakWorkingSetBytes"]);
        Assert.Equal("25", metrics["resource.client-a.workingSetGrowthBytes"]);
        Assert.Equal("260", metrics["resource.client-a.peakPrivateBytes"]);
        Assert.Equal("40", metrics["resource.client-a.privateGrowthBytes"]);
        Assert.Equal("9", metrics["resource.client-a.peakHandles"]);
        Assert.Equal("7", metrics["resource.client-a.peakThreads"]);
        Assert.Equal("20", metrics["resource.client-a.processorTimeMilliseconds"]);
        Assert.Equal("500", metrics["resource.client-a.sampledDurationMilliseconds"]);
    }
}
