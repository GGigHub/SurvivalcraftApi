using System.Globalization;

namespace Survivalcraft.Testing.Soak;

public sealed record SoakResourceBudget(
    long MaximumPeakWorkingSetBytes = 1536L * 1024 * 1024,
    long MaximumPeakPrivateBytes = 2048L * 1024 * 1024,
    long MaximumWorkingSetGrowthBytes = 768L * 1024 * 1024,
    int MaximumPeakHandles = 8192,
    int MaximumPeakThreads = 512,
    int MinimumSamples = 2,
    int MaximumPendingFaultPackets = 10_000,
    int MaximumPendingReliablePackets = 16_384,
    long MaximumPendingReliableBytes = 256L * 1024 * 1024) {

    public SoakResourceBudget Validate() {
        if (MaximumPeakWorkingSetBytes <= 0
            || MaximumPeakPrivateBytes <= 0
            || MaximumWorkingSetGrowthBytes < 0
            || MaximumPeakHandles <= 0
            || MaximumPeakThreads <= 0
            || MinimumSamples <= 0
            || MaximumPendingFaultPackets <= 0
            || MaximumPendingReliablePackets <= 0
            || MaximumPendingReliableBytes <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(SoakResourceBudget),
                "Soak resource limits must be positive (growth may be zero).");
        }
        return this;
    }
}

public sealed record SoakBudgetViolation(
    string Role,
    string Metric,
    long Actual,
    long Limit,
    string Message);

public static class SoakResourceBudgetEvaluator {
    private const string Prefix = "resource.";
    private const string SampleSuffix = ".samples";

    public static SoakBudgetViolation[] Evaluate(
        IReadOnlyDictionary<string, string> metrics,
        SoakResourceBudget budget) {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Validate();

        string[] roles = metrics.Keys
            .Where(key => key.StartsWith(Prefix, StringComparison.Ordinal)
                && key.EndsWith(SampleSuffix, StringComparison.Ordinal))
            .Select(key => key[Prefix.Length..^SampleSuffix.Length])
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (roles.Length == 0) {
            return [new SoakBudgetViolation(
                "*",
                "samples",
                0,
                budget.MinimumSamples,
                "Scenario emitted no process resource metrics.")];
        }

        List<SoakBudgetViolation> violations = [];
        foreach (string role in roles) {
            CheckMaximum(
                metrics,
                role,
                "samples",
                budget.MinimumSamples,
                violations,
                minimum: true);
            CheckMaximum(
                metrics,
                role,
                "peakWorkingSetBytes",
                budget.MaximumPeakWorkingSetBytes,
                violations);
            CheckMaximum(
                metrics,
                role,
                "peakPrivateBytes",
                budget.MaximumPeakPrivateBytes,
                violations);
            CheckMaximum(
                metrics,
                role,
                "workingSetGrowthBytes",
                budget.MaximumWorkingSetGrowthBytes,
                violations);
            CheckMaximum(
                metrics,
                role,
                "peakHandles",
                budget.MaximumPeakHandles,
                violations);
            CheckMaximum(
                metrics,
                role,
                "peakThreads",
                budget.MaximumPeakThreads,
                violations);
        }
        return violations.ToArray();
    }

    private static void CheckMaximum(
        IReadOnlyDictionary<string, string> metrics,
        string role,
        string metric,
        long limit,
        ICollection<SoakBudgetViolation> violations,
        bool minimum = false) {
        string key = $"{Prefix}{role}.{metric}";
        if (!metrics.TryGetValue(key, out string? text)
            || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) {
            violations.Add(new SoakBudgetViolation(
                role,
                metric,
                0,
                limit,
                $"Resource metric '{key}' is missing or invalid."));
            return;
        }

        bool failed = minimum ? value < limit : value > limit;
        if (failed) {
            violations.Add(new SoakBudgetViolation(
                role,
                metric,
                value,
                limit,
                minimum
                    ? $"Resource metric '{key}' sampled {value} times; minimum is {limit}."
                    : $"Resource metric '{key}' was {value}; maximum is {limit}."));
        }
    }
}
