using System.IO;
using Game.Network;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class ModNetworkProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void CatalogCanonicalizationNormalizesOrdersAndRejectsDuplicates() {
        ModNetworkChannelCatalog source = new([
            Channel("Zeta.Mod", "State", 2, ModNetworkDirection.ServerToClient, 512),
            Channel("alpha.mod", "Input", 1, ModNetworkDirection.ClientToServer, 64)
        ]);

        bool valid = ModNetworkProtocol.TryCanonicalizeCatalog(
            source,
            out ModNetworkChannelCatalog canonical,
            out string error);

        Assert.True(valid, error);
        Assert.Equal(
            ["alpha.mod:input", "zeta.mod:state"],
            canonical.Channels.Select(channel => channel.QualifiedName));

        source.Channels.Add(Channel("ALPHA.MOD", "INPUT", 1, ModNetworkDirection.ClientToServer, 64));
        Assert.False(ModNetworkProtocol.TryCanonicalizeCatalog(source, out _, out error));
        Assert.Contains("duplicate channel", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CatalogComparisonReportsMissingExtraAndEveryContractDifference() {
        ModNetworkChannelCatalog server = new([
            Channel("required.mod", "state", 1, ModNetworkDirection.ServerToClient, 512),
            Channel("shared.mod", "rpc", 2, ModNetworkDirection.Bidirectional, 1024)
        ]);
        ModNetworkChannelCatalog client = new([
            Channel("shared.mod", "rpc", 1, ModNetworkDirection.ClientToServer, 2048),
            Channel("client.mod", "extra", 1, ModNetworkDirection.ServerToClient, 64)
        ]);

        NetworkCompatibilityResult result = ModNetworkProtocol.EvaluateCatalogs(server, client);

        Assert.False(result.Accepted);
        Assert.Equal(
            [
                NetworkCompatibilityIssueCode.MissingClientChannel,
                NetworkCompatibilityIssueCode.ChannelVersionMismatch,
                NetworkCompatibilityIssueCode.ChannelDirectionMismatch,
                NetworkCompatibilityIssueCode.ChannelPayloadLimitMismatch,
                NetworkCompatibilityIssueCode.ExtraClientChannel
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void CatalogCodecRoundTripsCanonicalContractAndFingerprint() {
        ModNetworkChannelCatalog source = new([
            Channel("Example.Mod", "Echo", 3, ModNetworkDirection.Bidirectional, 4096)
        ]);

        byte[] encoded = ModNetworkProtocol.EncodeCatalog(source);
        ModNetworkChannelCatalog decoded = ModNetworkProtocol.DecodeCatalog(encoded);

        ModNetworkChannelDescriptor channel = Assert.Single(decoded.Channels);
        Assert.Equal("example.mod:echo", channel.QualifiedName);
        Assert.Equal(3, channel.Version);
        Assert.Equal(ModNetworkDirection.Bidirectional, channel.Directions);
        Assert.Equal(4096, channel.MaximumPayloadBytes);
        Assert.Equal(
            ModNetworkProtocol.ComputeCatalogFingerprint(source),
            ModNetworkProtocol.ComputeCatalogFingerprint(decoded));
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void CatalogCodecRejectsTruncationTrailingDataAndInvalidDirections() {
        byte[] encoded = ModNetworkProtocol.EncodeCatalog(new ModNetworkChannelCatalog([
            Channel("example.mod", "echo", 1, ModNetworkDirection.Bidirectional, 64)
        ]));

        Assert.Throws<InvalidDataException>(
            () => ModNetworkProtocol.DecodeCatalog(encoded[..^1]));
        Assert.Throws<InvalidDataException>(
            () => ModNetworkProtocol.DecodeCatalog([.. encoded, 0x7f]));
        Assert.Throws<InvalidDataException>(
            () => ModNetworkProtocol.EncodeCatalog(new ModNetworkChannelCatalog([
                Channel("example.mod", "echo", 1, ModNetworkDirection.None, 64)
            ])));
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void MessageEnvelopeCodecPreservesIdentityAndEnforcesGlobalPayloadLimit() {
        Guid messageId = Guid.NewGuid();
        ModNetworkEnvelope source = new(
            messageId,
            "Example.Mod",
            "Echo",
            4,
            [1, 2, 3, 4]);

        ModNetworkEnvelope decoded = ModNetworkProtocol.DecodeEnvelope(
            ModNetworkProtocol.EncodeEnvelope(source));

        Assert.Equal(messageId, decoded.MessageId);
        Assert.Equal("example.mod", decoded.PackageName);
        Assert.Equal("echo", decoded.ChannelName);
        Assert.Equal(4, decoded.ChannelVersion);
        Assert.Equal([1, 2, 3, 4], decoded.Payload);
        Assert.Throws<InvalidDataException>(() => ModNetworkProtocol.EncodeEnvelope(
            new ModNetworkEnvelope(
                Guid.NewGuid(),
                "example.mod",
                "echo",
                1,
                new byte[ModNetworkProtocol.MaximumPayloadBytes + 1])));
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void TrafficLimiterUsesDeterministicMessageByteAndWindowBounds() {
        ModNetworkTrafficLimiter limiter = new(
            maximumMessages: 2,
            maximumBytes: 5,
            windowMilliseconds: 1000);

        Assert.True(limiter.TryConsume(2, 100));
        Assert.True(limiter.TryConsume(3, 200));
        Assert.False(limiter.TryConsume(0, 300));
        Assert.False(limiter.TryConsume(1, 400));
        Assert.True(limiter.TryConsume(5, 1100));
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void MessageDeduplicatorRejectsDuplicatesAndEvictsOldestIdentity() {
        ModNetworkMessageDeduplicator deduplicator = new(capacity: 2);
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();

        Assert.True(deduplicator.TryRemember(first));
        Assert.False(deduplicator.TryRemember(first));
        Assert.True(deduplicator.TryRemember(second));
        Assert.True(deduplicator.TryRemember(third));
        Assert.True(deduplicator.TryRemember(first));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DescriptorValidationRejectsUnsafeIdentityVersionDirectionAndPayloadBounds() {
        ModNetworkChannelDescriptor[] invalid = [
            Channel("example/mod", "echo", 1, ModNetworkDirection.Bidirectional, 64),
            Channel("example.mod", "bad channel", 1, ModNetworkDirection.Bidirectional, 64),
            Channel("example.mod", "echo", 0, ModNetworkDirection.Bidirectional, 64),
            Channel("example.mod", "echo", 1, (ModNetworkDirection)4, 64),
            Channel("example.mod", "echo", 1, ModNetworkDirection.Bidirectional, 0),
            Channel(
                "example.mod",
                "echo",
                1,
                ModNetworkDirection.Bidirectional,
                ModNetworkProtocol.MaximumPayloadBytes + 1)
        ];

        foreach (ModNetworkChannelDescriptor descriptor in invalid) {
            Assert.False(ModNetworkProtocol.TryCanonicalizeDescriptor(
                descriptor,
                out _,
                out string error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        Assert.True(ModNetworkProtocol.TryCanonicalizeDescriptor(
            Channel(
                "example.mod",
                "echo",
                1,
                ModNetworkDirection.Bidirectional,
                ModNetworkProtocol.MaximumPayloadBytes),
            out _,
            out string validError),
            validError);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CatalogValidationEnforcesGlobalAndPerPackageChannelCounts() {
        ModNetworkChannelCatalog tooManyForPackage = new(
            Enumerable.Range(0, ModNetworkProtocol.MaximumChannelsPerPackage + 1)
                .Select(index => Channel(
                    "example.mod",
                    $"channel{index}",
                    1,
                    ModNetworkDirection.Bidirectional,
                    64)));
        Assert.False(ModNetworkProtocol.TryCanonicalizeCatalog(
            tooManyForPackage,
            out _,
            out string packageError));
        Assert.Contains("exceeds", packageError, StringComparison.Ordinal);

        ModNetworkChannelCatalog tooManyGlobally = new(
            Enumerable.Range(0, ModNetworkProtocol.MaximumChannelCount + 1)
                .Select(index => Channel(
                    $"mod{index}",
                    "channel",
                    1,
                    ModNetworkDirection.Bidirectional,
                    64)));
        Assert.False(ModNetworkProtocol.TryCanonicalizeCatalog(
            tooManyGlobally,
            out _,
            out string globalError));
        Assert.Contains("channel count", globalError, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void CatalogValidationRejectsUtf8CatalogThatExceedsWireLimit() {
        ModNetworkChannelCatalog catalog = new(
            Enumerable.Range(0, ModNetworkProtocol.MaximumChannelCount)
                .Select(index => Channel(
                    $"p{index:D3}{new string('界', 252)}",
                    "channel",
                    1,
                    ModNetworkDirection.Bidirectional,
                    64)));

        Assert.False(ModNetworkProtocol.TryCanonicalizeCatalog(
            catalog,
            out _,
            out string error));
        Assert.Contains("encoded catalog size", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void MessageEnvelopeCodecRejectsEmptyIdentityTruncationAndTrailingData() {
        Assert.Throws<InvalidDataException>(() => ModNetworkProtocol.EncodeEnvelope(
            new ModNetworkEnvelope(
                Guid.Empty,
                "example.mod",
                "echo",
                1,
                [])));

        byte[] encoded = ModNetworkProtocol.EncodeEnvelope(new ModNetworkEnvelope(
            Guid.NewGuid(),
            "example.mod",
            "echo",
            1,
            []));
        ModNetworkEnvelope decoded = ModNetworkProtocol.DecodeEnvelope(encoded);

        Assert.Empty(decoded.Payload);
        Assert.Throws<InvalidDataException>(
            () => ModNetworkProtocol.DecodeEnvelope(encoded[..^1]));
        Assert.Throws<InvalidDataException>(
            () => ModNetworkProtocol.DecodeEnvelope([.. encoded, 0x7f]));
    }

    private static ModNetworkChannelDescriptor Channel(
        string packageName,
        string channelName,
        int version,
        ModNetworkDirection directions,
        int maximumPayloadBytes) =>
        new(packageName, channelName, version, directions, maximumPayloadBytes);
}
