using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class FurnitureHammerPacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void EveryFurnitureHammerEventHasAStableBoundedWireShape() {
        byte[][] payloads = [
            Write(writer => {
                writer.Write((byte)0);
                writer.Write(3);
                writer.Write(new byte[] { 1, 2, 3 });
                writer.Write(12);
                writer.Write((byte)64);
                writer.Write(-8);
                writer.Write((byte)3);
            }),
            Write(writer => {
                writer.Write((byte)1);
                writer.Write(27);
                writer.Write(2);
                writer.Write(new byte[] { 9, 8 });
            }),
            Write(writer => {
                writer.Write((byte)2);
                writer.Write("rejected");
            })
        ];

        foreach (byte[] payload in payloads) {
            FurnitureHammerPacket packet = Decode(payload);
            Assert.Equal(payload, Encode(packet));
            Assert.Equal(NetworkChannels.Terrain, packet.Channel);
            Assert.Equal(NetworkState.ProjectLoaded, packet.MinNeedState);
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FurnitureHammerRejectsUnknownEventsAndOversizedDesignsBeforeAllocation() {
        byte[] unknown = Write(writer => writer.Write(byte.MaxValue));
        byte[] oversized = Write(writer => {
            writer.Write((byte)0);
            writer.Write(SubsystemFurnitureBlockBehavior.MaxNetworkFurnitureDesignDataLength + 1);
        });

        Assert.Throws<InvalidDataException>(() => Decode(unknown));
        Assert.Throws<InvalidOperationException>(() => Decode(oversized));
    }

    private static FurnitureHammerPacket Decode(byte[] payload) {
        FurnitureHammerPacket packet = new();
        using PacketStreamReader reader = new(payload);
        packet.Serialize(new PacketSerializer(reader));
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return packet;
    }

    private static byte[] Encode(FurnitureHammerPacket packet) {
        using PacketStreamWriter writer = new();
        packet.Serialize(new PacketSerializer(writer));
        return writer.Data();
    }

    private static byte[] Write(Action<PacketStreamWriter> write) {
        using PacketStreamWriter writer = new();
        write(writer);
        return writer.Data();
    }
}
