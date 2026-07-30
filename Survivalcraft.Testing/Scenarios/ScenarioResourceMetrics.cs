using System.Globalization;
using Survivalcraft.Testing.Processes;

namespace Survivalcraft.Testing.Scenarios;

public static class ScenarioResourceMetrics {
    public static void Capture(
        IDictionary<string, string> metrics,
        GameNodeProcess node) {
        ArgumentNullException.ThrowIfNull(node);
        node.CaptureResourceSample();
        Add(metrics, node.Role, node.ResourceSummary);
    }

    public static void Add(
        IDictionary<string, string> metrics,
        string role,
        ProcessResourceSummary summary) {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(summary);

        string prefix = $"resource.{role}.";
        metrics[prefix + "samples"] = Format(summary.SampleCount);
        metrics[prefix + "initialWorkingSetBytes"] = Format(summary.InitialWorkingSetBytes);
        metrics[prefix + "lastWorkingSetBytes"] = Format(summary.LastWorkingSetBytes);
        metrics[prefix + "peakWorkingSetBytes"] = Format(summary.PeakWorkingSetBytes);
        metrics[prefix + "workingSetGrowthBytes"] = Format(summary.WorkingSetGrowthBytes);
        metrics[prefix + "initialPrivateBytes"] = Format(summary.InitialPrivateMemoryBytes);
        metrics[prefix + "lastPrivateBytes"] = Format(summary.LastPrivateMemoryBytes);
        metrics[prefix + "peakPrivateBytes"] = Format(summary.PeakPrivateMemoryBytes);
        metrics[prefix + "privateGrowthBytes"] = Format(summary.PrivateMemoryGrowthBytes);
        metrics[prefix + "peakHandles"] = Format(summary.PeakHandleCount);
        metrics[prefix + "peakThreads"] = Format(summary.PeakThreadCount);
        metrics[prefix + "processorTimeMilliseconds"] =
            Format(summary.ProcessorTimeMilliseconds);
        metrics[prefix + "sampledDurationMilliseconds"] =
            Format(summary.SampledDurationMilliseconds);
    }

    private static string Format(long value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
