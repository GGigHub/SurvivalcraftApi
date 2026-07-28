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
    string? Note = null);

public static class CoverageManifestValidator {
    private static readonly HashSet<string> AllowedStatuses =
        new(["planned", "covered", "quarantined"], StringComparer.Ordinal);

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
        if (manifest.SchemaVersion != 1) {
            errors.Add($"schemaVersion must be 1, got {manifest.SchemaVersion}");
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
            if (string.IsNullOrWhiteSpace(feature.ProductionEntry)) {
                errors.Add($"{prefix}: productionEntry is required");
            }
            if (feature.Status == "covered") {
                Require(feature.ActionCommand, prefix, "actionCommand", errors);
                if (feature.SnapshotDomains == null || feature.SnapshotDomains.Length == 0) {
                    errors.Add($"{prefix}: covered feature requires snapshotDomains");
                }
                Require(feature.DirectTest, prefix, "directTest", errors);
                Require(feature.RelayTest, prefix, "relayTest", errors);
                Require(feature.AuthorityTest, prefix, "authorityTest", errors);
            }
            if (feature.Status == "quarantined") {
                Require(feature.QuarantineIssue, prefix, "quarantineIssue", errors);
                Require(feature.Note, prefix, "note", errors);
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
