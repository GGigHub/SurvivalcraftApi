using System.Text.Json;
using System.Text.RegularExpressions;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Coverage;

public sealed record ProductionSurfaceManifest(
    int SchemaVersion,
    ProductionSurface[] Surfaces);

public sealed record ProductionSurface(
    string SurfaceId,
    string Kind,
    string Symbol,
    string FeatureId,
    string SourcePath);

public static partial class ProductionSurfaceCoverage {
    private static readonly HashSet<string> AllowedKinds =
        new(["hook", "packet"], StringComparer.Ordinal);

    public static async Task<ProductionSurfaceManifest> LoadAsync(
        string path,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = File.OpenRead(Path.GetFullPath(path));
        return await JsonSerializer.DeserializeAsync<ProductionSurfaceManifest>(
            stream,
            ControlProtocol.JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Production surface manifest '{path}' is empty or invalid.");
    }

    public static string[] Validate(
        CoverageManifest coverage,
        ProductionSurfaceManifest inventory) {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(inventory);
        List<string> errors = [];
        if (inventory.SchemaVersion != 1) {
            errors.Add($"surface schemaVersion must be 1, got {inventory.SchemaVersion}");
        }
        HashSet<string> featureIds = new(
            (coverage.Features ?? []).Select(feature => feature.FeatureId),
            StringComparer.Ordinal);
        ProductionSurface[] surfaces = inventory.Surfaces ?? [];
        foreach (IGrouping<string, ProductionSurface> duplicate in surfaces
                     .Where(surface => !string.IsNullOrWhiteSpace(surface.SurfaceId))
                     .GroupBy(surface => surface.SurfaceId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)) {
            errors.Add($"duplicate production surface '{duplicate.Key}'");
        }

        foreach (ProductionSurface surface in surfaces) {
            string prefix = string.IsNullOrWhiteSpace(surface.SurfaceId)
                ? "<missing surfaceId>"
                : surface.SurfaceId;
            if (!AllowedKinds.Contains(surface.Kind ?? string.Empty)) {
                errors.Add($"{prefix}: invalid kind '{surface.Kind}'");
            }
            if (string.IsNullOrWhiteSpace(surface.Symbol)) {
                errors.Add($"{prefix}: symbol is required");
            }
            string expectedId = $"{surface.Kind}:{surface.Symbol}";
            if (!string.Equals(surface.SurfaceId, expectedId, StringComparison.Ordinal)) {
                errors.Add($"{prefix}: surfaceId must be '{expectedId}'");
            }
            if (!featureIds.Contains(surface.FeatureId ?? string.Empty)) {
                errors.Add($"{prefix}: unknown featureId '{surface.FeatureId}'");
            }
            if (string.IsNullOrWhiteSpace(surface.SourcePath)
                || Path.IsPathRooted(surface.SourcePath)
                || surface.SourcePath.Contains('\\')) {
                errors.Add($"{prefix}: sourcePath must be a repository-relative forward-slash path");
            }
        }
        return errors.ToArray();
    }

    public static ProductionSurface[] Discover(string repositoryRoot) {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string root = Path.GetFullPath(repositoryRoot);
        string loaderPath = Path.Combine(
            root,
            "Survivalcraft.Multiplayer",
            "NetworkModLoader.cs");
        string packetRoot = Path.Combine(
            root,
            "Survivalcraft.Multiplayer",
            "Network",
            "Packet");
        if (!File.Exists(loaderPath) || !Directory.Exists(packetRoot)) {
            throw new DirectoryNotFoundException(
                $"Multiplayer production sources were not found below '{root}'.");
        }

        List<ProductionSurface> result = [];
        string loaderSource = File.ReadAllText(loaderPath);
        Match hookBlock = HookBlockRegex().Match(loaderSource);
        if (!hookBlock.Success) {
            throw new InvalidDataException(
                $"Could not locate NetworkModLoader hookNames in '{loaderPath}'.");
        }
        foreach (Match match in HookNameRegex().Matches(hookBlock.Groups["body"].Value)) {
            string symbol = match.Groups["name"].Value;
            result.Add(new ProductionSurface(
                $"hook:{symbol}",
                "hook",
                symbol,
                string.Empty,
                NormalizeRelativePath(root, loaderPath)));
        }

        foreach (string packetPath in Directory.EnumerateFiles(
                     packetRoot,
                     "*.cs",
                     SearchOption.AllDirectories)) {
            string source = File.ReadAllText(packetPath);
            foreach (Match match in PacketDeclarationRegex().Matches(source)) {
                string symbol = match.Groups["name"].Value;
                result.Add(new ProductionSurface(
                    $"packet:{symbol}",
                    "packet",
                    symbol,
                    string.Empty,
                    NormalizeRelativePath(root, packetPath)));
            }
        }

        return result
            .OrderBy(surface => surface.SurfaceId, StringComparer.Ordinal)
            .ToArray();
    }

    public static string[] CompareInventory(
        ProductionSurfaceManifest inventory,
        IEnumerable<ProductionSurface> discovered) {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(discovered);
        Dictionary<string, ProductionSurface> expected = (inventory.Surfaces ?? [])
            .GroupBy(surface => surface.SurfaceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, ProductionSurface> actual = discovered
            .GroupBy(surface => surface.SurfaceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        List<string> errors = [];
        foreach (string missing in actual.Keys.Except(expected.Keys, StringComparer.Ordinal)) {
            errors.Add($"unmapped production surface '{missing}'");
        }
        foreach (string stale in expected.Keys.Except(actual.Keys, StringComparer.Ordinal)) {
            errors.Add($"stale production surface '{stale}'");
        }
        foreach (string shared in expected.Keys.Intersect(actual.Keys, StringComparer.Ordinal)) {
            if (!string.Equals(
                    expected[shared].SourcePath,
                    actual[shared].SourcePath,
                    StringComparison.Ordinal)) {
                errors.Add(
                    $"{shared}: sourcePath changed from '{expected[shared].SourcePath}' "
                    + $"to '{actual[shared].SourcePath}'");
            }
        }
        return errors.OrderBy(error => error, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    [GeneratedRegex(
        @"string\[\]\s+hookNames\s*=\s*\[(?<body>[\s\S]*?)\];",
        RegexOptions.CultureInvariant)]
    private static partial Regex HookBlockRegex();

    [GeneratedRegex(
        "\\\"(?<name>(?:IsNetworkMode|OnEntityAdd|OnEntityRemove|OnNetwork[^\\\"]+))\\\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex HookNameRegex();

    [GeneratedRegex(
        @"\b(?:class|record\s+class)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*Packet)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex PacketDeclarationRegex();
}
