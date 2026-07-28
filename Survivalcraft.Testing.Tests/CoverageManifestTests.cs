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
            && feature.DirectTest != null
            && feature.RelayTest != null);
        Assert.Contains(manifest.Features, feature =>
            feature.FeatureId == "player.place-dig-use"
            && feature.DirectTest != null
            && feature.RelayTest != null
            && feature.AuthorityTest != null);
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

        string[] errors = CoverageManifestValidator.Validate(new CoverageManifest(1, [invalid]));

        Assert.Contains(errors, error => error.Contains("actionCommand", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("snapshotDomains", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("directTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("relayTest", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("authorityTest", StringComparison.Ordinal));
    }
}
