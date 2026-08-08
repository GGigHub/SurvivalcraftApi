using Engine;
using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class NetworkDigConfirmationTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void RecoveryPacketsPreserveCorrelationResultAndTerrainOrdering() {
        PlayerDigRequestPacket request = RoundTrip(
            new PlayerDigRequestPacket {
                m_fromPlayerIndex = 7,
                ActionId = 41,
                CellFace = new CellFace(12, 64, -8, 3),
                OldValue = 321,
                ExpectedValue = 0
            },
            static () => new PlayerDigRequestPacket());
        PlayerDigConfirmationPacket confirmation = RoundTrip(
            new PlayerDigConfirmationPacket {
                m_fromPlayerIndex = 7,
                ActionId = 41,
                CellFace = new CellFace(12, 64, -8, 3),
                ExpectedValue = 0,
                ActualValue = 321,
                Success = false,
                FailureReason = PlayerDigRequestFailureReason.ValueMismatch
            },
            static () => new PlayerDigConfirmationPacket());

        Assert.Equal((7, 41, 12, 64, -8, 3, 321, 0),
            (request.m_fromPlayerIndex, request.ActionId,
                request.CellFace.X, request.CellFace.Y, request.CellFace.Z,
                request.CellFace.Face, request.OldValue, request.ExpectedValue));
        Assert.Equal((7, 41, 0, 321, false, PlayerDigRequestFailureReason.ValueMismatch),
            (confirmation.m_fromPlayerIndex, confirmation.ActionId,
                confirmation.ExpectedValue, confirmation.ActualValue,
                confirmation.Success, confirmation.FailureReason));
        Assert.Equal(NetworkChannels.Terrain, request.Channel);
        Assert.Equal(NetworkChannels.Terrain, confirmation.Channel);
        Assert.Equal(NetworkState.Playing, request.MinNeedState);
        Assert.Equal(NetworkState.Playing, confirmation.MinNeedState);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ServerRequestWindowIsBoundedDeduplicatedExpiringAndPlayerScoped() {
        NetworkDigServerRequestWindow window = new(maximumTotal: 3, maximumPerPlayer: 2);
        PlayerDigRequestPacket first = Request(1, new CellFace(4, 65, 6, 1));
        PlayerDigRequestPacket second = Request(2, new CellFace(4, 65, 6, 1));
        PlayerDigRequestPacket otherPlayer = Request(1, new CellFace(8, 65, 9, 1));

        Assert.True(window.TryAdd(first, playerIndex: 10, createdFrame: 100));
        Assert.False(window.TryAdd(first, playerIndex: 10, createdFrame: 101));
        Assert.True(window.TryAdd(second, playerIndex: 10, createdFrame: 102));
        Assert.False(window.TryAdd(Request(3, first.CellFace), 10, 103));
        Assert.True(window.TryAdd(otherPlayer, playerIndex: 11, createdFrame: 104));
        Assert.False(window.TryAdd(Request(1, new CellFace(9, 65, 9, 1)), 12, 105));
        Assert.Equal(3, window.Count);
        Assert.Equal(2, window.CountForPlayer(10));

        NetworkDigServerRequest[] matched = window.TakeMatching(10, first.CellFace);
        Assert.Equal(new[] { 1, 2 }, matched.Select(item => item.Packet.ActionId));
        Assert.Equal(1, window.Count);

        Assert.Empty(window.TakeExpired(currentFrame: 283, timeoutFrames: 180));
        NetworkDigServerRequest expired = Assert.Single(
            window.TakeExpired(currentFrame: 284, timeoutFrames: 180));
        Assert.Equal(11, expired.PlayerIndex);
        Assert.Equal(0, window.Count);

        Assert.True(window.TryAdd(Request(4, first.CellFace), 10, 300));
        Assert.True(window.TryAdd(Request(5, first.CellFace), 11, 300));
        window.ClearPlayer(10);
        Assert.False(window.Contains(10, 4));
        Assert.True(window.Contains(11, 5));
        window.Clear();
        Assert.Equal(0, window.Count);
    }

    private static PlayerDigRequestPacket Request(int actionId, CellFace cellFace) => new() {
        m_fromPlayerIndex = 10,
        ActionId = actionId,
        CellFace = cellFace,
        OldValue = 321,
        ExpectedValue = 0
    };

    private static TPacket RoundTrip<TPacket>(TPacket source, Func<TPacket> create)
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
