using System.Text.Json;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Coverage;

public sealed record CoverageManifest(
    int SchemaVersion,
    CoverageFeature[] Features);

public sealed record CoverageFeature(
    string FeatureId,
    string Status,
    string ProductionEntry,
    string? FixtureCommand,
    string? ActionCommand,
    string[] SnapshotDomains,
    string? DirectTest,
    string? RelayTest,
    string? LateJoinTest,
    string? ReconnectTest,
    string? AuthorityTest,
    string? FaultTest,
    string? PersistenceTest,
    string? QuarantineIssue = null,
    string? Note = null,
    string Risk = "medium",
    string Confidence = "planned",
    string? ProductionPathTest = null,
    string? ObserverTest = null,
    string? ConcurrencyTest = null,
    string? MutationTest = null,
    string? SoakTest = null,
    string? RealityTest = null,
    string[]? Gaps = null);

public static class CoverageManifestValidator {
    private static readonly HashSet<string> AllowedStatuses =
        new(["planned", "partial", "covered", "quarantined", "excluded"], StringComparer.Ordinal);

    private static readonly HashSet<string> AllowedRisks =
        new(["critical", "high", "medium", "low"], StringComparer.Ordinal);

    private static readonly Dictionary<string, int> ConfidenceRanks = new(StringComparer.Ordinal) {
        ["planned"] = 0,
        ["partial"] = 1,
        ["behavioral"] = 2,
        ["mutation"] = 3,
        ["soak"] = 4,
        ["reality"] = 5
    };

    public static async Task<CoverageManifest> LoadAsync(
        string path,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = File.OpenRead(Path.GetFullPath(path));
        CoverageManifest manifest = await JsonSerializer.DeserializeAsync<CoverageManifest>(
            stream,
            ControlProtocol.JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Coverage manifest '{path}' is empty or invalid.");
        string[] errors = Validate(manifest);
        if (errors.Length > 0) {
            throw new InvalidDataException(
                $"Coverage manifest '{path}' is invalid: {string.Join("; ", errors)}");
        }
        return manifest;
    }

    public static string[] Validate(CoverageManifest manifest) {
        ArgumentNullException.ThrowIfNull(manifest);
        List<string> errors = [];
        if (manifest.SchemaVersion != 2) {
            errors.Add($"schemaVersion must be 2, got {manifest.SchemaVersion}");
        }
        CoverageFeature[] features = manifest.Features ?? [];
        foreach (IGrouping<string, CoverageFeature> duplicate in features
                     .Where(feature => !string.IsNullOrWhiteSpace(feature.FeatureId))
                     .GroupBy(feature => feature.FeatureId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)) {
            errors.Add($"duplicate featureId '{duplicate.Key}'");
        }

        foreach (CoverageFeature feature in features) {
            string prefix = string.IsNullOrWhiteSpace(feature.FeatureId)
                ? "<missing featureId>"
                : feature.FeatureId;
            if (string.IsNullOrWhiteSpace(feature.FeatureId)) {
                errors.Add("featureId is required");
            }
            if (!AllowedStatuses.Contains(feature.Status ?? string.Empty)) {
                errors.Add($"{prefix}: invalid status '{feature.Status}'");
            }
            if (!AllowedRisks.Contains(feature.Risk ?? string.Empty)) {
                errors.Add($"{prefix}: invalid risk '{feature.Risk}'");
            }
            if (!ConfidenceRanks.TryGetValue(feature.Confidence ?? string.Empty, out int confidenceRank)) {
                errors.Add($"{prefix}: invalid confidence '{feature.Confidence}'");
                confidenceRank = -1;
            }
            if (string.IsNullOrWhiteSpace(feature.ProductionEntry)) {
                errors.Add($"{prefix}: productionEntry is required");
            }
            if (feature.Status == "covered") {
                if (confidenceRank < ConfidenceRanks["behavioral"]) {
                    errors.Add($"{prefix}: covered feature requires behavioral or stronger confidence");
                }
                Require(feature.ActionCommand, prefix, "actionCommand", errors);
                if (feature.SnapshotDomains == null || feature.SnapshotDomains.Length == 0) {
                    errors.Add($"{prefix}: covered feature requires snapshotDomains");
                }
                Require(feature.ProductionPathTest, prefix, "productionPathTest", errors);
                Require(feature.DirectTest, prefix, "directTest", errors);
                Require(feature.RelayTest, prefix, "relayTest", errors);
                Require(feature.ObserverTest, prefix, "observerTest", errors);
                Require(feature.AuthorityTest, prefix, "authorityTest", errors);
                Require(feature.LateJoinTest, prefix, "lateJoinTest", errors);
                Require(feature.ReconnectTest, prefix, "reconnectTest", errors);
                Require(feature.FaultTest, prefix, "faultTest", errors);
                Require(feature.PersistenceTest, prefix, "persistenceTest", errors);
                if (feature.Gaps is { Length: > 0 }) {
                    errors.Add($"{prefix}: covered feature cannot retain declared gaps");
                }
            }
            if (feature.Status is "planned" or "partial") {
                if (feature.Gaps == null || feature.Gaps.Length == 0
                    || feature.Gaps.Any(string.IsNullOrWhiteSpace)) {
                    errors.Add($"{prefix}: {feature.Status} feature requires explicit non-empty gaps");
                }
                string expectedConfidence = feature.Status == "planned" ? "planned" : "partial";
                if (!string.Equals(feature.Confidence, expectedConfidence, StringComparison.Ordinal)) {
                    errors.Add($"{prefix}: {feature.Status} feature requires confidence '{expectedConfidence}'");
                }
            }
            if (feature.Status == "quarantined") {
                Require(feature.QuarantineIssue, prefix, "quarantineIssue", errors);
                Require(feature.Note, prefix, "note", errors);
            }
            if (feature.Status == "excluded") {
                Require(feature.Note, prefix, "note", errors);
            }
            if (confidenceRank >= ConfidenceRanks["mutation"]) {
                Require(feature.MutationTest, prefix, "mutationTest", errors);
            }
            if (confidenceRank >= ConfidenceRanks["soak"]) {
                Require(feature.SoakTest, prefix, "soakTest", errors);
            }
            if (confidenceRank >= ConfidenceRanks["reality"]) {
                Require(feature.RealityTest, prefix, "realityTest", errors);
            }
        }
        return errors.ToArray();
    }

    private static void Require(
        string? value,
        string featureId,
        string field,
        List<string> errors) {
        if (string.IsNullOrWhiteSpace(value)) {
            errors.Add($"{featureId}: {field} is required");
        }
    }
}
