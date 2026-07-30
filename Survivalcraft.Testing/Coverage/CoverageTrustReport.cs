namespace Survivalcraft.Testing.Coverage;

public sealed record CoverageTrustReport(
    int SchemaVersion,
    int TotalFeatures,
    IReadOnlyDictionary<string, int> Statuses,
    IReadOnlyDictionary<string, int> Risks,
    IReadOnlyDictionary<string, int> ConfidenceLevels,
    int HookSurfaces,
    int PacketSurfaces,
    FeatureTrustSummary[] Features,
    string[] ValidationErrors) {
    public bool IsStructurallyValid => ValidationErrors.Length == 0;
}

public sealed record FeatureTrustSummary(
    string FeatureId,
    string Status,
    string Risk,
    string Confidence,
    string[] Gaps);

public static class CoverageTrustReportBuilder {
    public static CoverageTrustReport Build(
        CoverageManifest coverage,
        ProductionSurfaceManifest inventory,
        IEnumerable<ProductionSurface> discovered) {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(discovered);
        ProductionSurface[] actual = discovered.ToArray();
        string[] validationErrors = CoverageManifestValidator.Validate(coverage)
            .Concat(ProductionSurfaceCoverage.Validate(coverage, inventory))
            .Concat(ProductionSurfaceCoverage.CompareInventory(inventory, actual))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(error => error, StringComparer.Ordinal)
            .ToArray();
        CoverageFeature[] features = coverage.Features ?? [];
        return new CoverageTrustReport(
            coverage.SchemaVersion,
            features.Length,
            CountBy(features, feature => feature.Status),
            CountBy(features, feature => feature.Risk),
            CountBy(features, feature => feature.Confidence),
            actual.Count(surface => surface.Kind == "hook"),
            actual.Count(surface => surface.Kind == "packet"),
            features
                .OrderByDescending(feature => RiskRank(feature.Risk))
                .ThenBy(feature => feature.FeatureId, StringComparer.Ordinal)
                .Select(feature => new FeatureTrustSummary(
                    feature.FeatureId,
                    feature.Status,
                    feature.Risk,
                    feature.Confidence,
                    feature.Gaps ?? []))
                .ToArray(),
            validationErrors);
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<CoverageFeature> features,
        Func<CoverageFeature, string> selector) =>
        features
            .GroupBy(selector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static int RiskRank(string risk) => risk switch {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };
}
