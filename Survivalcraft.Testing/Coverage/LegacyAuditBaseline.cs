using System.Text.Json;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Coverage;

public sealed record LegacyAuditBaseline(
    int SchemaVersion,
    string Policy,
    LegacyAuditBaselineGroup[] Groups);

public sealed record LegacyAuditBaselineGroup(
    string FeatureId,
    string Disposition,
    string Reason,
    string[] Tests,
    string[] Replacements);

public static class LegacyAuditBaselineValidator {
    private static readonly HashSet<string> AllowedDispositions =
        new(["superseded", "coverage-gap"], StringComparer.Ordinal);

    public static async Task<LegacyAuditBaseline> LoadAsync(
        string path,
        CoverageManifest coverage,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = File.OpenRead(Path.GetFullPath(path));
        LegacyAuditBaseline baseline = await JsonSerializer.DeserializeAsync<LegacyAuditBaseline>(
            stream,
            ControlProtocol.JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Legacy audit baseline '{path}' is empty or invalid.");
        string[] errors = Validate(baseline, coverage);
        if (errors.Length > 0) {
            throw new InvalidDataException(
                $"Legacy audit baseline '{path}' is invalid: {string.Join("; ", errors)}");
        }
        return baseline;
    }

    public static string[] Validate(
        LegacyAuditBaseline baseline,
        CoverageManifest coverage) {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(coverage);
        List<string> errors = [];
        if (baseline.SchemaVersion != 1) {
            errors.Add($"schemaVersion must be 1, got {baseline.SchemaVersion}");
        }
        if (!string.Equals(baseline.Policy, "new-failures-block", StringComparison.Ordinal)) {
            errors.Add("policy must be 'new-failures-block'");
        }

        Dictionary<string, CoverageFeature> features = (coverage.Features ?? [])
            .Where(feature => !string.IsNullOrWhiteSpace(feature.FeatureId))
            .ToDictionary(feature => feature.FeatureId, StringComparer.Ordinal);
        HashSet<string> tests = new(StringComparer.Ordinal);
        LegacyAuditBaselineGroup[] groups = baseline.Groups ?? [];
        if (groups.Length == 0) {
            errors.Add("at least one legacy baseline group is required");
        }
        foreach (LegacyAuditBaselineGroup? group in groups) {
            if (group == null) {
                errors.Add("legacy baseline groups cannot contain null entries");
                continue;
            }
            string prefix = string.IsNullOrWhiteSpace(group.FeatureId)
                ? "<missing featureId>"
                : group.FeatureId;
            if (!AllowedDispositions.Contains(group.Disposition ?? string.Empty)) {
                errors.Add($"{prefix}: invalid disposition '{group.Disposition}'");
            }
            if (string.IsNullOrWhiteSpace(group.Reason)) {
                errors.Add($"{prefix}: reason is required");
            }
            if (group.Tests == null || group.Tests.Length == 0) {
                errors.Add($"{prefix}: at least one test is required");
            }
            foreach (string test in group.Tests ?? []) {
                if (string.IsNullOrWhiteSpace(test) || test.Count(character => character == '.') < 2) {
                    errors.Add($"{prefix}: invalid fully-qualified test name '{test}'");
                }
                else if (!tests.Add(test)) {
                    errors.Add($"duplicate legacy test '{test}'");
                }
            }

            string[] replacements = group.Replacements ?? [];
            if (!features.TryGetValue(group.FeatureId ?? string.Empty, out CoverageFeature? feature)) {
                errors.Add($"{prefix}: featureId does not exist in the coverage manifest");
                continue;
            }
            if (group.Disposition == "superseded") {
                if (replacements.Length == 0 || replacements.Any(string.IsNullOrWhiteSpace)) {
                    errors.Add($"{prefix}: superseded tests require explicit replacements");
                }
                HashSet<string> declaredEvidence = new(
                    new[] { feature.DirectTest, feature.RelayTest }
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Cast<string>(),
                    StringComparer.Ordinal);
                foreach (string replacement in replacements) {
                    if (!declaredEvidence.Contains(replacement)) {
                        errors.Add($"{prefix}: replacement '{replacement}' is not a declared Direct/Relay test");
                    }
                }
            }
            else if (group.Disposition == "coverage-gap") {
                if (string.Equals(feature.Status, "covered", StringComparison.Ordinal)) {
                    errors.Add($"{prefix}: coverage-gap cannot target a covered feature");
                }
                if (replacements.Length > 0) {
                    errors.Add($"{prefix}: coverage-gap cannot claim replacement tests");
                }
            }
        }
        return errors.ToArray();
    }

    public static string[] FlattenTests(LegacyAuditBaseline baseline) =>
        (baseline.Groups ?? [])
        .SelectMany(group => group.Tests ?? [])
        .OrderBy(test => test, StringComparer.Ordinal)
        .ToArray();
}
