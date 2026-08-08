using Survivalcraft.Testing.Coverage;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class CoverageManifestTests {
    [Fact]
    [Trait("Layer", "L0")]
    public async Task RepositoryCoverageManifestIsStructurallyValidAndUnique() {
        string path = Path.Combine(AppContext.BaseDirectory, "Coverage", "core-multiplayer.json");

        CoverageManifest manifest = await CoverageManifestValidator.LoadAsync(path);

        Assert.NotEmpty(manifest.Features);
        Assert.Contains(manifest.Features, feature =>
            feature.FeatureId == "player.creative-fly"
            && feature.Status == "partial"
            && feature.DirectTest != null
            && feature.RelayTest != null
            && feature.Gaps is { Length: > 0 });
        CoverageFeature[] covered = manifest.Features
            .Where(feature => feature.Status == "covered")
            .ToArray();
        Assert.Equal(16, covered.Length);
        Assert.Equal(4, manifest.Features.Count(feature => feature.Status == "planned"));
        Assert.Equal(1, manifest.Features.Count(feature => feature.Status == "partial"));
        Assert.All(covered, feature => {
            Assert.NotNull(feature.ProductionPathTest);
            Assert.NotNull(feature.DirectTest);
            Assert.NotNull(feature.RelayTest);
            Assert.NotNull(feature.ObserverTest);
            Assert.NotNull(feature.LateJoinTest);
            Assert.NotNull(feature.ReconnectTest);
            Assert.NotNull(feature.AuthorityTest);
            Assert.NotNull(feature.FaultTest);
            Assert.NotNull(feature.PersistenceTest);
            Assert.True(feature.Gaps == null || feature.Gaps.Length == 0);
        });
        Assert.Contains(manifest.Features, feature =>
            feature.FeatureId == "pickable-projectile.lifecycle"
            && feature.Status == "covered"
            && feature.Confidence == "mutation"
            && feature.MutationTest == "pickable-stale-create-resurrection-sentry"
            && feature.DirectTest != null
            && feature.RelayTest != null
            && (feature.Gaps == null || feature.Gaps.Length == 0));
        Assert.All(
            manifest.Features.Where(feature => feature.FeatureId is
                "network.session-compatibility" or
                "api.mod-channel" or
                "player.session-lifecycle"),
            feature => {
                Assert.Equal("covered", feature.Status);
                Assert.Equal("soak", feature.Confidence);
                Assert.NotNull(feature.MutationTest);
                Assert.NotNull(feature.SoakTest);
                Assert.True(feature.Gaps == null || feature.Gaps.Length == 0);
            });
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task RepositoryProductionSurfaceInventoryMatchesEveryHookAndPacket() {
        string coveragePath = Path.Combine(
            AppContext.BaseDirectory,
            "Coverage",
            "core-multiplayer.json");
        string surfacePath = Path.Combine(
            AppContext.BaseDirectory,
            "Coverage",
            "production-surfaces.json");
        CoverageManifest coverage = await CoverageManifestValidator.LoadAsync(coveragePath);
        ProductionSurfaceManifest inventory = await ProductionSurfaceCoverage.LoadAsync(surfacePath);

        Assert.Empty(ProductionSurfaceCoverage.Validate(coverage, inventory));
        ProductionSurface[] discovered = ProductionSurfaceCoverage.Discover(FindRepositoryRoot());
        Assert.Equal(104, discovered.Count(surface => surface.Kind == "hook"));
        Assert.Equal(110, discovered.Count(surface => surface.Kind == "packet"));
        Assert.Empty(ProductionSurfaceCoverage.CompareInventory(inventory, discovered));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CoveredFeatureRequiresActionObservabilityAndAuthorityEvidence() {
        CoverageFeature invalid = new(
            "feature",
            "covered",
            "Production.Entry",
            null,
            null,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        string[] errors = CoverageManifestValidator.Validate(new CoverageManifest(2, [invalid]));

        Assert.Contains(errors, error => error.Contains("actionCommand", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("snapshotDomains", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("directTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("relayTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("authorityTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("productionPathTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("observerTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("lateJoinTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("reconnectTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("faultTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("persistenceTest", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void InventoryComparisonRejectsBothUnmappedAndStaleSurfaces() {
        ProductionSurfaceManifest inventory = new(1, [
            new("hook:Old", "hook", "Old", "feature", "NetworkModLoader.cs")
        ]);
        ProductionSurface[] discovered = [
            new("hook:New", "hook", "New", string.Empty, "NetworkModLoader.cs")
        ];

        string[] errors = ProductionSurfaceCoverage.CompareInventory(inventory, discovered);

        Assert.Contains(errors, error => error.Contains("unmapped production surface 'hook:New'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("stale production surface 'hook:Old'", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task TrustReportExposesRiskConfidenceAndOpenGapsWithoutInventingAPercentage() {
        string coveragePath = Path.Combine(
            AppContext.BaseDirectory,
            "Coverage",
            "core-multiplayer.json");
        string surfacePath = Path.Combine(
            AppContext.BaseDirectory,
            "Coverage",
            "production-surfaces.json");
        CoverageManifest coverage = await CoverageManifestValidator.LoadAsync(coveragePath);
        ProductionSurfaceManifest inventory = await ProductionSurfaceCoverage.LoadAsync(surfacePath);
        ProductionSurface[] discovered = ProductionSurfaceCoverage.Discover(FindRepositoryRoot());

        CoverageTrustReport report = CoverageTrustReportBuilder.Build(
            coverage,
            inventory,
            discovered);

        Assert.True(report.IsStructurallyValid);
        Assert.Equal(coverage.Features.Length, report.TotalFeatures);
        Assert.Equal(104, report.HookSurfaces);
        Assert.Equal(110, report.PacketSurfaces);
        Assert.True(report.Statuses["covered"] > 0);
        Assert.True(report.Statuses["planned"] > 0);
        Assert.Contains(report.Features, feature =>
            feature.FeatureId == "player.session-lifecycle"
            && feature.Risk == "critical"
            && feature.Status == "covered"
            && feature.Confidence == "soak"
            && feature.Gaps.Length == 0);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task LegacyFailureBaselineIsExplicitFeatureLinkedAndCannotGrowSilently() {
        string coveragePath = Path.Combine(
            AppContext.BaseDirectory,
            "Coverage",
            "core-multiplayer.json");
        string baselinePath = Path.Combine(
            AppContext.BaseDirectory,
            "Coverage",
            "legacy-known-failures.json");
        CoverageManifest coverage = await CoverageManifestValidator.LoadAsync(coveragePath);
        LegacyAuditBaseline baseline = await LegacyAuditBaselineValidator.LoadAsync(
            baselinePath,
            coverage);

        string[] tests = LegacyAuditBaselineValidator.FlattenTests(baseline);
        Assert.Equal(20, tests.Length);
        Assert.Equal(tests.Length, tests.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(baseline.Groups, group =>
            group.Disposition == "coverage-gap"
            && group.FeatureId == "player.progression-vitals"
            && group.Replacements.Length == 0);
        Assert.All(
            baseline.Groups.Where(group => group.Disposition == "superseded"),
            group => Assert.NotEmpty(group.Replacements));
    }

    private static string FindRepositoryRoot() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SurvivalcraftApi.sln"))) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
