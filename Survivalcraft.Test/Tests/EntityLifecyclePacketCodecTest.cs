using Engine;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class EntityLifecyclePacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void RemovalPacketPreservesLifecycleRevision() {
        EntityRemovePacket decoded = RoundTrip(
            new EntityRemovePacket(417, 9_223_372_036_854L),
            static () => new EntityRemovePacket());

        Assert.Equal(417, decoded.m_entityId);
        Assert.Equal(9_223_372_036_854L, decoded.m_revision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LoadStampListPreservesEntityRevisions() {
        List<EntityLifecycleStamp> stamps = [
            new EntityLifecycleStamp { EntityId = 12, Revision = 101 },
            new EntityLifecycleStamp { EntityId = 99, Revision = 4_294_967_311L }
        ];

        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            EntityLoadPacket.SerializeLifecycleStamps(
                new PacketSerializer(writer),
                ref stamps);
            payload = writer.Data();
        }

        List<EntityLifecycleStamp> decoded = [];
        using PacketStreamReader reader = new(payload);
        EntityLoadPacket.SerializeLifecycleStamps(
            new PacketSerializer(reader),
            ref decoded);

        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        Assert.Collection(
            decoded,
            stamp => Assert.Equal((12, 101L), (stamp.EntityId, stamp.Revision)),
            stamp => Assert.Equal((99, 4_294_967_311L), (stamp.EntityId, stamp.Revision)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65_537)]
    [Trait("Layer", "L0")]
    public void LoadStampListRejectsInvalidCountBeforeReadingStamps(int count) {
        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            writer.Write(count);
            payload = writer.Data();
        }
        using PacketStreamReader reader = new(payload);
        List<EntityLifecycleStamp> stamps = [];

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            EntityLoadPacket.SerializeLifecycleStamps(
                new PacketSerializer(reader),
                ref stamps));

        Assert.Contains(count.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void BehaviorPacketPreservesBodyAndLifecycleState() {
        BehaviorSyncPacket source = new() {
            m_sendTime = 1234.5,
            m_items = [
                new BehaviorSyncPacket.BehaviorSyncItem {
                    EntityId = 81,
                    LifecycleRevision = 7,
                    BodyPosition = new Vector3(1.25f, 72.5f, -4.75f),
                    BodyRotation = Quaternion.CreateFromYawPitchRoll(0.5f, -0.25f, 0.1f),
                    BodyVelocity = new Vector3(2f, 0.5f, -1f),
                    ActiveBehaviorIndex = -1
                }
            ]
        };

        BehaviorSyncPacket decoded = RoundTrip(
            source,
            static () => new BehaviorSyncPacket());
        BehaviorSyncPacket.BehaviorSyncItem item = Assert.Single(decoded.m_items);

        Assert.Equal(source.m_sendTime, decoded.m_sendTime);
        Assert.Equal(81, item.EntityId);
        Assert.Equal(7, item.LifecycleRevision);
        Assert.Equal(source.m_items[0].BodyPosition, item.BodyPosition);
        Assert.Equal(source.m_items[0].BodyRotation, item.BodyRotation);
        Assert.Equal(source.m_items[0].BodyVelocity, item.BodyVelocity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65_537)]
    [Trait("Layer", "L0")]
    public void BehaviorPacketRejectsInvalidItemCountBeforeReadingItems(int count) {
        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            writer.Write(1234.5);
            writer.Write(count);
            payload = writer.Data();
        }
        using PacketStreamReader reader = new(payload);
        BehaviorSyncPacket packet = new();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            packet.Serialize(new PacketSerializer(reader)));

        Assert.Contains(count.ToString(), exception.Message, StringComparison.Ordinal);
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
