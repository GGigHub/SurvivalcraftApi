using Engine;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class TerrainRevisionPacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void SingleCellPacketPreservesRevision() {
        TerrainChangeCellPacket decoded = RoundTrip(
            new TerrainChangeCellPacket(129, 72, -33, 481, 9_223_372_036_854L),
            static () => new TerrainChangeCellPacket());

        Assert.Equal(129, decoded.m_X);
        Assert.Equal(72, decoded.m_Y);
        Assert.Equal(-33, decoded.m_Z);
        Assert.Equal(481, decoded.m_value);
        Assert.Equal(9_223_372_036_854L, decoded.m_revision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void CellListPacketPreservesPerChunkRevisions() {
        TerrainChangeCellListPacket decoded = RoundTrip(
            new TerrainChangeCellListPacket([
                new CellChange { X = 16, Y = 64, Z = 32, Value = 7, Revision = 101 },
                new CellChange { X = 17, Y = 65, Z = 33, Value = 8, Revision = 102 }
            ]),
            static () => new TerrainChangeCellListPacket());

        Assert.Collection(
            decoded.cellChanges,
            change => {
                Assert.Equal((16, 64, 32, 7, 101L),
                    (change.X, change.Y, change.Z, change.Value, change.Revision));
            },
            change => {
                Assert.Equal((17, 65, 33, 8, 102L),
                    (change.X, change.Y, change.Z, change.Value, change.Revision));
            });
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ChunkSnapshotPacketPreservesBaselineRevisionAndDeltas() {
        TerrainPacketBase.TerrainChunkSyncData data = new() {
            Coords = new Point2(-7, 23),
            Revision = 4_294_967_311L,
            CellChanges = [
                new TerrainPacketBase.CellValueChange { Index = 3, Value = 404 },
                new TerrainPacketBase.CellValueChange { Index = ushort.MaxValue, Value = 505 }
            ],
            ShaftChanges = [
                new TerrainPacketBase.ShaftValueChange { Index = 12, Value = 606 }
            ]
        };

        TerrainSyncChunkListPacket decoded = RoundTrip(
            new TerrainSyncChunkListPacket([data]),
            static () => new TerrainSyncChunkListPacket());

        TerrainPacketBase.TerrainChunkSyncData actual = Assert.Single(decoded.SyncData);
        Assert.Equal(data.Coords, actual.Coords);
        Assert.Equal(data.Revision, actual.Revision);
        Assert.Equal(
            data.CellChanges.Select(change => (change.Index, change.Value)),
            actual.CellChanges.Select(change => (change.Index, change.Value)));
        Assert.Equal(
            data.ShaftChanges.Select(change => (change.Index, change.Value)),
            actual.ShaftChanges.Select(change => (change.Index, change.Value)));
    }

    private static TPacket RoundTrip<TPacket>(
        TPacket source,
        Func<TPacket> create)
        where TPacket : Packet {
        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            source.Serialize(new PacketSerializer(writer));
            payload = writer.Data();
        }

        TPacket decoded = create();
        using PacketStreamReader reader = new(payload);
        decoded.Serialize(new PacketSerializer(reader));
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return decoded;
    }
}
