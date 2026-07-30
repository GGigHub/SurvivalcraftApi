using Engine;
using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test.Tests;

public sealed class PickableProjectileProtocolTest {
    [Fact]
    public void PickablePacketsPreserveLifecycleRevisions() {
        Pickable sourcePickable = new() {
            Id = 73,
            Value = Terrain.MakeBlockValue(1),
            Count = 4,
            Position = new Vector3(1.25f, 72.5f, -3.75f),
            Velocity = new Vector3(0.5f, 1.5f, -0.25f),
            FlyToPosition = new Vector3(3f, 73f, -2f)
        };

        PickableCreatePacket create = RoundTrip(
            new PickableCreatePacket {
                m_pickable = sourcePickable,
                m_revision = 41
            },
            static () => new PickableCreatePacket());
        Assert.Equal(73, create.m_pickable.Id);
        Assert.Equal(4, create.m_pickable.Count);
        Assert.Equal(41, create.m_revision);

        PickableUpdatePacket update = RoundTrip(
            new PickableUpdatePacket {
                m_pickables = [sourcePickable],
                m_lifecycleStamps = [new PickableLifecycleStamp {
                    PickableId = 73,
                    Revision = 42
                }]
            },
            static () => new PickableUpdatePacket());
        Assert.Equal(73, Assert.Single(update.m_pickables).Id);
        Assert.Equal(42, Assert.Single(update.m_lifecycleStamps).Revision);

        PickableSetFlyToPositionPacket attract = RoundTrip(
            new PickableSetFlyToPositionPacket {
                m_id = 73,
                m_flyToPosition = sourcePickable.FlyToPosition,
                m_revision = 43
            },
            static () => new PickableSetFlyToPositionPacket());
        Assert.Equal(73, attract.m_id);
        Assert.Equal(43, attract.m_revision);
        Assert.Equal(sourcePickable.FlyToPosition, attract.m_flyToPosition);

        PickableDeletePacket remove = RoundTrip(
            new PickableDeletePacket {
                m_id = 73,
                PlaySound = true,
                m_revision = 44
            },
            static () => new PickableDeletePacket());
        Assert.Equal(73, remove.m_id);
        Assert.True(remove.PlaySound);
        Assert.Equal(44, remove.m_revision);
    }

    [Fact]
    public void PickableListPacketsPreservePerItemLifecycleRevisions() {
        Pickable first = new() {
            Id = 10,
            Value = Terrain.MakeBlockValue(2),
            Count = 1,
            Position = new Vector3(1f, 2f, 3f)
        };
        Pickable second = new() {
            Id = 11,
            Value = Terrain.MakeBlockValue(3),
            Count = 2,
            Position = new Vector3(4f, 5f, 6f)
        };
        List<PickableLifecycleStamp> stamps = [
            new() { PickableId = 10, Revision = 7 },
            new() { PickableId = 11, Revision = 9 }
        ];

        PickableCreateListPacket creates = RoundTrip(
            new PickableCreateListPacket {
                m_pickables = [first, second],
                m_lifecycleStamps = stamps
            },
            static () => new PickableCreateListPacket());
        Assert.True(new[] { 10, 11 }.SequenceEqual(
            creates.m_pickables.Select(item => item.Id)));
        Assert.True(new[] { 7L, 9L }.SequenceEqual(
            creates.m_lifecycleStamps.Select(item => item.Revision)));

        PickableDeleteListPacket deletes = RoundTrip(
            new PickableDeleteListPacket {
                m_pickables = [first, second],
                m_lifecycleStamps = stamps
            },
            static () => new PickableDeleteListPacket());
        Assert.True(new[] { 10, 11 }.SequenceEqual(
            deletes.m_pickables.Select(item => item.Id)));
        Assert.True(new[] { 7L, 9L }.SequenceEqual(
            deletes.m_lifecycleStamps.Select(item => item.Revision)));

        PickableSyncListPacket baseline = RoundTrip(
            new PickableSyncListPacket {
                m_pickables = [first, second],
                m_lifecycleStamps = stamps
            },
            static () => new PickableSyncListPacket());
        Assert.True(new[] { 10, 11 }.SequenceEqual(
            baseline.m_pickables.Select(item => item.Id)));
        Assert.True(new[] { 7L, 9L }.SequenceEqual(
            baseline.m_lifecycleStamps.Select(item => item.Revision)));
    }

    [Fact]
    public void PickableLifecycleRejectsResurrectionAndDoesNotPoisonMissingUpdates() {
        PickableLifecycleWindow window = new();
        Assert.True(window.ReceiveCreate(1).Apply);
        Assert.False(window.ReceiveCreate(1).Apply);
        Assert.True(window.ReceiveUpdate(2).Apply);
        Assert.True(window.ReceiveRemoval(3).Apply);
        Assert.False(window.ReceiveCreate(1).Apply);
        Assert.False(window.ReceiveUpdate(4).Apply);
        Assert.True(window.ReceiveCreate(4).Apply);
        Assert.True(window.Present);
        Assert.Equal(4, window.AppliedRevision);

        PickableLifecycleWindow missing = new();
        Assert.False(missing.ReceiveUpdate(8).Apply);
        Assert.False(missing.HasState);
        Assert.True(missing.ReceiveCreate(7).Apply);
    }

    [Fact]
    public void ProjectilePacketPreservesEventIdentity() {
        ProjectilePacket decoded = RoundTrip(
            new ProjectilePacket {
                m_value = Terrain.MakeBlockValue(80),
                m_position = new Vector3(3f, 90f, 4f),
                m_velocity = new Vector3(12f, 1f, -2f),
                m_angularVelocity = new Vector3(0f, 2f, 0f),
                m_trailOfffset = new Vector3(0.1f, 0.2f, 0.3f),
                m_ownerId = 17,
                m_isFireProjectile = true,
                m_eventId = 9_223_372_036_854L
            },
            static () => new ProjectilePacket());

        Assert.Equal(9_223_372_036_854L, decoded.m_eventId);
        Assert.Equal(17, decoded.m_ownerId);
        Assert.True(decoded.m_isFireProjectile);
        Assert.Equal(new Vector3(12f, 1f, -2f), decoded.m_velocity);
    }

    [Fact]
    public void ProjectileEventWindowAllowsReorderButRejectsDuplicatesAndExpiredEvents() {
        ProjectileEventWindow window = new();
        Assert.True(window.TryAccept(2));
        Assert.True(window.TryAccept(1));
        Assert.False(window.TryAccept(2));
        Assert.Equal(2, window.AcceptedEvents);
        Assert.Equal(1, window.IgnoredDuplicateEvents);

        Assert.True(window.TryAccept(ProjectileEventWindow.RetentionWindow + 10L));
        Assert.False(window.TryAccept(1));
        Assert.Equal(1, window.IgnoredExpiredEvents);
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
