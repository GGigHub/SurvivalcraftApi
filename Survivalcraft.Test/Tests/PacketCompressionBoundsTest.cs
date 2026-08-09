using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class PacketCompressionBoundsTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void DecompressionStopsBeforeCrossingConfiguredOutputLimit() {
        byte[] uncompressed = Enumerable.Repeat((byte)0x5A, 4096).ToArray();
        byte[] compressed;
        using (MemoryStream input = new(uncompressed)) {
            compressed = PacketStreamHelper.Compress(input);
        }

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PacketStreamHelper.Decompress(compressed, 1024));

        Assert.Contains("1024-byte", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CompressionRoundTripPreservesPayloadWithinGlobalLimits() {
        byte[] expected = Enumerable.Range(0, 8192)
            .Select(index => (byte)(index * 31))
            .ToArray();
        byte[] compressed;
        using (MemoryStream input = new(expected)) {
            compressed = PacketStreamHelper.Compress(input);
        }

        byte[] actual = PacketStreamHelper.Decompress(compressed);

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DecompressionRejectsACompressedFrameAboveTheIngressLimit() {
        byte[] oversized = new byte[PacketStreamHelper.MaximumCompressedLength + 1];

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PacketStreamHelper.Decompress(oversized));

        Assert.Contains(
            PacketStreamHelper.MaximumCompressedLength.ToString(),
            exception.Message,
            StringComparison.Ordinal);
    }
}
