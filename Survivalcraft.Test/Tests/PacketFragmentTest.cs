using Game;
using Game.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Survivalcraft.Test;

[Collection(ModNetworkManagerCollection.Name)]
public sealed class PacketFragmentTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void ValidChunksReassembleAndReleaseBufferedState() {
        int previousChunkSize = SettingsManager.NetworkChunkSize;
        SettingsManager.NetworkChunkSize = PacketFragment.MinChunkSize;
        try {
            PacketFragment fragment = new();
            byte[] first = Enumerable.Range(0, PacketFragment.MinChunkSize)
                .Select(index => (byte)index)
                .ToArray();
            byte[] last = [201, 202, 203];

            Assert.False(fragment.TryProcessIncoming(
                BuildChunkFrame(3, first.Length + last.Length, 0, first),
                "peer",
                out _));
            Assert.True(fragment.TryProcessIncoming(
                BuildChunkFrame(3, first.Length + last.Length, first.Length, last),
                "peer",
                out byte[] payload));

            Assert.Equal(first.Concat(last), payload);
            Assert.Equal(0, fragment.ActiveStateCount);
            Assert.Equal(0, fragment.BufferedBytes);
        }
        finally {
            SettingsManager.NetworkChunkSize = previousChunkSize;
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void OversizedChunkFrameIsRejectedBeforeAllocatingState() {
        PacketFragment fragment = new();
        NetDataReader reader = BuildChunkFrame(
            messageId: 1,
            totalLength: PacketFragment.MaxPayloadLength + 1,
            offset: 0,
            body: [1]);

        bool completed = fragment.TryProcessIncoming(reader, "peer", out byte[] payload);

        Assert.False(completed);
        Assert.Null(payload);
        Assert.Equal(0, fragment.ActiveStateCount);
        Assert.Equal(0, fragment.BufferedBytes);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void PartialChunksMustHaveExactAlignedShape() {
        PacketFragment fragment = new();
        NetDataReader reader = BuildChunkFrame(
            messageId: 2,
            totalLength: PacketFragment.MinChunkSize + 1,
            offset: 0,
            body: [1]);

        bool completed = fragment.TryProcessIncoming(reader, "peer", out byte[] payload);

        Assert.False(completed);
        Assert.Null(payload);
        Assert.Equal(0, fragment.ActiveStateCount);
        Assert.Equal(0, fragment.BufferedBytes);
    }

    [Fact]
    [Trait("Layer", "L1")]
    public void PerSourceReassemblyStateCountIsBounded() {
        int previousChunkSize = SettingsManager.NetworkChunkSize;
        SettingsManager.NetworkChunkSize = PacketFragment.MinChunkSize;
        try {
            PacketFragment fragment = new();
            byte[] firstChunk = new byte[PacketFragment.MinChunkSize];
            for (int index = 0; index < PacketFragment.MaxActiveStatesPerSource; index++) {
                Assert.False(fragment.TryProcessIncoming(
                    BuildChunkFrame(index + 10, firstChunk.Length + 1, 0, firstChunk),
                    "peer",
                    out _));
            }

            Assert.False(fragment.TryProcessIncoming(
                BuildChunkFrame(100, firstChunk.Length + 1, 0, firstChunk),
                "peer",
                out _));
            Assert.Equal(PacketFragment.MaxActiveStatesPerSource, fragment.ActiveStateCount);
            Assert.Equal(
                PacketFragment.MaxActiveStatesPerSource * (firstChunk.Length + 1L),
                fragment.BufferedBytes);
        }
        finally {
            SettingsManager.NetworkChunkSize = previousChunkSize;
        }
    }

    private static NetDataReader BuildChunkFrame(
        int messageId,
        int totalLength,
        int offset,
        byte[] body) {
        NetDataWriter writer = new();
        writer.Put(PacketFragment.ChunkMagic);
        writer.Put(PacketFragment.ChunkVersion);
        writer.Put(messageId);
        writer.Put(totalLength);
        writer.Put(offset);
        writer.Put((ushort)body.Length);
        writer.PutBytesWithLength(body);
        return new NetDataReader(writer.CopyData());
    }
}
