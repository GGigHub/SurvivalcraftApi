using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class PlayerCreativeFlyPacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void RequestAndStateEventsHaveStableWireShapes() {
        byte[][] payloads = [
            Write(writer => {
                writer.Write((byte)0);
                writer.Write(7);
                writer.Write(4_294_967_801L);
                writer.Write(true);
            }),
            Write(writer => {
                writer.Write((byte)1);
                writer.Write(7);
                writer.Write(4_294_967_801L);
                writer.Write((byte)CreativeFlyResultCode.Accepted);
                writer.Write(true);
                writer.Write(91);
                writer.Write(12L);
                writer.Write(34L);
            })
        ];

        foreach (byte[] payload in payloads) {
            PlayerCreativeFlyPacket packet = Decode(payload);
            Assert.Equal(payload, Encode(packet));
            Assert.Equal(NetworkState.ProjectLoaded, packet.MinNeedState);
        }
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void UnknownCreativeFlyEventsAreRejected() {
        byte[] payload = Write(writer => writer.Write(byte.MaxValue));

        Assert.Throws<InvalidDataException>(() => Decode(payload));
    }

    [Theory]
    [InlineData((byte)CreativeFlyResultCode.None)]
    [InlineData(byte.MaxValue)]
    [Trait("Layer", "L0")]
    public void UnknownOrNonTerminalCreativeFlyResultsAreRejected(byte resultCode) {
        byte[] payload = Write(writer => {
            writer.Write((byte)1);
            writer.Write(7);
            writer.Write(41L);
            writer.Write(resultCode);
        });

        Assert.Throws<InvalidDataException>(() => Decode(payload));
    }

    private static PlayerCreativeFlyPacket Decode(byte[] payload) {
        PlayerCreativeFlyPacket packet = new();
        using PacketStreamReader reader = new(payload);
        packet.Serialize(new PacketSerializer(reader));
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return packet;
    }

    private static byte[] Encode(PlayerCreativeFlyPacket packet) {
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
