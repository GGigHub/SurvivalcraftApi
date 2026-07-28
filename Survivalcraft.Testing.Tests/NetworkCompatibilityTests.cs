using System.IO;
using Game.Network;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class NetworkCompatibilityTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void CanonicalizationNormalizesAndOrdersManifestDeterministically() {
        NetworkCompatibilityManifest source = Manifest(
            new NetworkModIdentity(" Zeta.Mod ", " 2.0 ", " 1.9.3 "),
            new NetworkModIdentity("alpha.MOD", "1.0", "1.9.2"));

        bool valid = NetworkCompatibility.TryCanonicalize(
            source,
            out NetworkCompatibilityManifest canonical,
            out string error);

        Assert.True(valid, error);
        Assert.Equal(["alpha.mod", "zeta.mod"], canonical.Mods.Select(mod => mod.PackageName));
        Assert.Equal("2.0", canonical.Mods[1].Version);
        Assert.Equal("1.9.3", canonical.ApiVersion);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DuplicatePackageIsRejectedCaseInsensitively() {
        NetworkCompatibilityManifest source = Manifest(
            new NetworkModIdentity("Example.Mod", "1", "1.9.3"),
            new NetworkModIdentity("example.mod", "1", "1.9.3"));

        NetworkCompatibilityResult result = NetworkCompatibility.Evaluate(
            Manifest(),
            source);

        Assert.False(result.Accepted);
        Assert.Equal(NetworkCompatibilityIssueCode.InvalidClientManifest, result.PrimaryCode);
        Assert.Contains("duplicate package", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ProtocolApiAndCapabilitiesProduceStructuredIssues() {
        NetworkCompatibilityManifest server = Manifest();
        NetworkCompatibilityManifest client = Manifest();
        client.ProtocolRevision++;
        client.ApiVersion = "1.9.2";
        client.Capabilities = NetworkCapabilities.ModPacketChannel;

        NetworkCompatibilityResult result = NetworkCompatibility.Evaluate(server, client);

        Assert.False(result.Accepted);
        Assert.Equal(
            [
                NetworkCompatibilityIssueCode.ProtocolMismatch,
                NetworkCompatibilityIssueCode.ApiVersionMismatch,
                NetworkCompatibilityIssueCode.MissingCapabilities
            ],
            result.Issues.Select(issue => issue.Code));
        Assert.All(result.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message)));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ModManifestReportsMissingExtraAndVersionDifferences() {
        NetworkCompatibilityManifest server = Manifest(
            new NetworkModIdentity("required.mod", "2.0", "1.9.3"),
            new NetworkModIdentity("versioned.mod", "3.0", "1.9.3"));
        NetworkCompatibilityManifest client = Manifest(
            new NetworkModIdentity("versioned.mod", "2.5", "1.9.2"),
            new NetworkModIdentity("client.only", "1.0", "1.9.3"));

        NetworkCompatibilityResult result = NetworkCompatibility.Evaluate(server, client);

        Assert.False(result.Accepted);
        Assert.Equal(
            [
                NetworkCompatibilityIssueCode.MissingClientMod,
                NetworkCompatibilityIssueCode.ModVersionMismatch,
                NetworkCompatibilityIssueCode.ModApiVersionMismatch,
                NetworkCompatibilityIssueCode.ExtraClientMod
            ],
            result.Issues.Select(issue => issue.Code));
        Assert.Equal("required.mod", result.Issues[0].PackageName);
        Assert.Equal("client.only", result.Issues[^1].PackageName);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ExactCanonicalManifestIsAccepted() {
        NetworkCompatibilityManifest server = Manifest(
            new NetworkModIdentity("B.Mod", "2", "1.9.3"),
            new NetworkModIdentity("a.mod", "1", "1.9.3"));
        NetworkCompatibilityManifest client = Manifest(
            new NetworkModIdentity("A.MOD", "1", "1.9.3"),
            new NetworkModIdentity("b.mod", "2", "1.9.3"));

        NetworkCompatibilityResult result = NetworkCompatibility.Evaluate(server, client);

        Assert.True(result.Accepted);
        Assert.Empty(result.Issues);
        Assert.Equal(NetworkCompatibilityIssueCode.None, result.PrimaryCode);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void ManifestWireCodecRoundTripsEveryHandshakeField() {
        NetworkCompatibilityManifest expected = Manifest(
            new NetworkModIdentity("example.mod", "1.2.3", "1.9.3"));

        byte[] payload = NetworkCompatibilityWireCodec.EncodeManifest(expected);
        NetworkCompatibilityManifest actual = NetworkCompatibilityWireCodec.DecodeManifest(payload);

        Assert.Equal(expected.ProtocolRevision, actual.ProtocolRevision);
        Assert.Equal(expected.ApiVersion, actual.ApiVersion);
        Assert.Equal(expected.Capabilities, actual.Capabilities);
        NetworkModIdentity mod = Assert.Single(actual.Mods);
        Assert.Equal("example.mod", mod.PackageName);
        Assert.Equal("1.2.3", mod.Version);
        Assert.Equal("1.9.3", mod.ApiVersion);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void ResultWireCodecPreservesMachineReadableDiagnostics() {
        NetworkCompatibilityResult expected = NetworkCompatibility.Evaluate(
            Manifest(new NetworkModIdentity("required.mod", "2", "1.9.3")),
            Manifest());

        byte[] payload = NetworkCompatibilityWireCodec.EncodeResult(expected);
        NetworkCompatibilityResult actual = NetworkCompatibilityWireCodec.DecodeResult(payload);

        Assert.False(actual.Accepted);
        NetworkCompatibilityIssue issue = Assert.Single(actual.Issues);
        Assert.Equal(NetworkCompatibilityIssueCode.MissingClientMod, issue.Code);
        Assert.Equal("required.mod", issue.PackageName);
        Assert.Equal(expected.Summary, actual.Summary);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void WireCodecRejectsTruncatedAndTrailingPayloads() {
        byte[] payload = NetworkCompatibilityWireCodec.EncodeManifest(Manifest());
        byte[] truncated = payload[..^1];
        byte[] trailing = [.. payload, 0x7f];

        Assert.Throws<InvalidDataException>(
            () => NetworkCompatibilityWireCodec.DecodeManifest(truncated));
        Assert.Throws<InvalidDataException>(
            () => NetworkCompatibilityWireCodec.DecodeManifest(trailing));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void RequiredCapabilitiesCannotBeDisabledByServerAdvertisement() {
        NetworkCompatibilityManifest server = Manifest();
        NetworkCompatibilityManifest client = Manifest();
        server.Capabilities = NetworkCapabilities.None;
        client.Capabilities = NetworkCapabilities.None;

        NetworkCompatibilityResult result = NetworkCompatibility.Evaluate(server, client);

        Assert.False(result.Accepted);
        Assert.Equal(NetworkCompatibilityIssueCode.MissingCapabilities, result.PrimaryCode);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void HandshakeTimeoutProducesStructuredDiagnostic() {
        NetworkCompatibilityResult result =
            NetworkCompatibility.CreateHandshakeTimeoutResult(15);

        Assert.False(result.Accepted);
        Assert.Equal(NetworkCompatibilityIssueCode.HandshakeTimeout, result.PrimaryCode);
        Assert.Contains("15 seconds", result.Summary, StringComparison.Ordinal);
    }

    private static NetworkCompatibilityManifest Manifest(params NetworkModIdentity[] mods) =>
        new(
            NetworkCompatibility.ProtocolRevision,
            "1.9.3",
            NetworkCompatibility.CurrentCapabilities,
            mods);
}
