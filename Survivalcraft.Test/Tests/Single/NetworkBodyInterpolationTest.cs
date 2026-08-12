using Engine;
using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class NetworkBodyInterpolationTest : IDisposable {
    public void Dispose() {
        NetworkBodyInterpolation.Clear();
    }

    [Fact]
    public void ClientPlayerMovementSnapshotsAreSmoothedForObserver() {
        ComponentBody observerBody = CreateBody(new Vector3(0f, 64f, 0f));

        PushSnapshot(observerBody, 0.0, new Vector3(0f, 64f, 0f), new Vector3(3f, 0f, 0f), NetworkBodyInterpolation.RemotePlayerDelay);
        PushSnapshot(observerBody, 0.5, new Vector3(1.5f, 64f, 0f), new Vector3(3f, 0f, 0f), NetworkBodyInterpolation.RemotePlayerDelay);
        PushSnapshot(observerBody, 1.0, new Vector3(3f, 64f, 0f), new Vector3(3f, 0f, 0f), NetworkBodyInterpolation.RemotePlayerDelay);

        NetworkBodyInterpolation.Update(observerBody, 0.25f);

        Assert.InRange(observerBody.Position.X, 1.4f, 1.6f);
        Assert.Equal(64f, observerBody.Position.Y);
        Assert.True(observerBody.Position.X < 3f, $"Expected smoothed playback before latest client position. Actual={observerBody.Position}.");
    }

    [Fact]
    public void ServerBodyMovementSnapshotsAreSmoothedForClient() {
        ComponentBody clientBody = CreateBody(new Vector3(20f, 70f, -8f));

        PushSnapshot(clientBody, 0.0, new Vector3(20f, 70f, -8f), new Vector3(0f, 0f, 8f), NetworkBodyInterpolation.RemoteBodyDelay);
        PushSnapshot(clientBody, 0.5, new Vector3(20f, 70f, -4f), new Vector3(0f, 0f, 8f), NetworkBodyInterpolation.RemoteBodyDelay);
        PushSnapshot(clientBody, 1.0, new Vector3(20f, 70f, 0f), new Vector3(0f, 0f, 8f), NetworkBodyInterpolation.RemoteBodyDelay);

        NetworkBodyInterpolation.Update(clientBody, 0.25f);

        Assert.InRange(clientBody.Position.Z, -4.4f, -3.6f);
        Assert.Equal(20f, clientBody.Position.X);
        Assert.True(clientBody.Position.Z < 0f, $"Expected smoothed playback before latest server position. Actual={clientBody.Position}.");
    }

    [Fact]
    public void LiveSnapshotsPrimeBeforeFollowingLatestTimeline() {
        ComponentBody body = CreateBody(new Vector3(0f, 64f, 0f));

        for (int i = 0; i <= 4; i++) {
            double time = i * 0.1;
            PushSnapshot(body, time, new Vector3((float)time, 64f, 0f), Vector3.UnitX, NetworkBodyInterpolation.RemoteBodyDelay);
            NetworkBodyInterpolation.Update(body, 0.1f);
        }

        NetworkBodyInterpolation.BodyInterpolationState state = NetworkBodyInterpolation.GetOrCreateState(
            body,
            NetworkBodyInterpolation.RemoteBodyDelay);
        Assert.False(state.HasPlaybackTime);
        Assert.Equal(0f, body.Position.X);

        PushSnapshot(body, 0.5, new Vector3(0.5f, 64f, 0f), Vector3.UnitX, NetworkBodyInterpolation.RemoteBodyDelay);
        NetworkBodyInterpolation.Update(body, 0.1f);

        Assert.True(state.HasPlaybackTime);
        Assert.Equal(0.0, state.PlaybackTime);
        Assert.Equal(0f, body.Position.X);

        PushSnapshot(body, 0.6, new Vector3(0.6f, 64f, 0f), Vector3.UnitX, NetworkBodyInterpolation.RemoteBodyDelay);
        NetworkBodyInterpolation.Update(body, 0.1f);

        Assert.Equal(0.1, state.PlaybackTime, 3);
        Assert.InRange(body.Position.X, 0.09f, 0.11f);
    }

    [Fact]
    public void BehaviorStateFollowsBodyPlaybackTimeline() {
        ComponentBody body = CreateBody(new Vector3(0f, 64f, 0f));
        NetworkBehaviorState behaviorState = new();

        PushSnapshot(body, 0.0, new Vector3(0f, 64f, 0f), Vector3.Zero, NetworkBodyInterpolation.RemoteBodyDelay);
        NetworkBodyInterpolation.PushSnapshot(
            body,
            positionChanged: true,
            new Vector3(1f, 64f, 0f),
            rotationChanged: false,
            Quaternion.Identity,
            velocityChanged: false,
            Vector3.Zero,
            0.5,
            NetworkBodyInterpolation.RemoteBodyDelay,
            behaviorState: behaviorState,
            sequence: 1);
        PushSnapshot(body, 1.0, new Vector3(2f, 64f, 0f), Vector3.Zero, NetworkBodyInterpolation.RemoteBodyDelay);

        NetworkBodyInterpolation.Update(body, 0.25f);
        NetworkBodyInterpolation.BodyInterpolationState interpolationState = NetworkBodyInterpolation.GetOrCreateState(
            body,
            NetworkBodyInterpolation.RemoteBodyDelay);
        Assert.True(interpolationState.HasLastBehaviorState);
        Assert.Equal(0.5, interpolationState.LastBehaviorStateTime);
        Assert.InRange(body.Position.X, 0.9f, 1.1f);

        NetworkBodyInterpolation.Update(body, 0.25f);
        Assert.True(interpolationState.HasLastBehaviorState);
        Assert.Equal(0.5, interpolationState.LastBehaviorStateTime);
        Assert.InRange(body.Position.X, 0.9f, 1.1f);
    }

    [Fact]
    public void BehaviorTimelineWorksWithoutPositionSnapshot() {
        ComponentBody body = CreateBody(new Vector3(0f, 64f, 0f));
        NetworkBehaviorState firstState = new();
        NetworkBehaviorState secondState = new();
        NetworkBehaviorEvent behaviorEvent = new() {
            Time = 0.0,
            Sequence = 1,
            Type = NetworkBehaviorEventType.StubbornSteedHeadShake,
            Amount = 1f
        };

        NetworkBodyInterpolation.PushSnapshot(
            body,
            positionChanged: false,
            Vector3.Zero,
            rotationChanged: false,
            Quaternion.Identity,
            velocityChanged: false,
            Vector3.Zero,
            0.0,
            NetworkBodyInterpolation.RemoteBodyDelay,
            firstState,
            [behaviorEvent],
            sequence: 1);
        NetworkBodyInterpolation.PushSnapshot(
            body,
            positionChanged: false,
            Vector3.Zero,
            rotationChanged: false,
            Quaternion.Identity,
            velocityChanged: false,
            Vector3.Zero,
            1.0,
            NetworkBodyInterpolation.RemoteBodyDelay,
            secondState,
            sequence: 2);

        NetworkBodyInterpolation.Update(body, 0.25f);
        NetworkBodyInterpolation.Update(body, 0.25f);

        NetworkBodyInterpolation.BodyInterpolationState interpolationState = NetworkBodyInterpolation.GetOrCreateState(
            body,
            NetworkBodyInterpolation.RemoteBodyDelay);
        Assert.True(interpolationState.HasLastBehaviorState);
        Assert.True(interpolationState.HasLastBehaviorEvent);
        Assert.Equal(0.0, interpolationState.LastBehaviorEventTime);
    }

    [Fact]
    public void EntityBodyUpdatePacketRoundTripsBehaviorEvents() {
        EntityBodyUpdatePacket packet = new() {
            SendTime = 12.5,
            Sequence = 7,
            m_items = [new EntityBodyUpdatePacket.EntityBodySyncItem {
                EntityId = 42,
                PositionChanged = true,
                Position = new Vector3(1f, 2f, 3f),
                HasBehavior = true,
                Behavior = new NetworkBehaviorState {
                    HasLocomotion = true,
                    TurnOrder = new Vector2(0.5f, -0.25f)
                },
                BehaviorEvents = [new NetworkBehaviorEvent {
                    Time = 12.25,
                    Sequence = 9,
                    Type = NetworkBehaviorEventType.HumanRow,
                    Value = true,
                    SecondaryValue = false
                }]
            }]
        };

        PacketStreamWriter writer = new();
        packet.Serialize(new PacketSerializer(writer));
        EntityBodyUpdatePacket decoded = new();
        decoded.Serialize(new PacketSerializer(new PacketStreamReader(writer.Data())));

        Assert.Equal(packet.SendTime, decoded.SendTime);
        Assert.Equal(packet.Sequence, decoded.Sequence);
        Assert.Single(decoded.m_items);
        EntityBodyUpdatePacket.EntityBodySyncItem item = decoded.m_items[0];
        Assert.Equal(42, item.EntityId);
        Assert.Equal(new Vector2(0.5f, -0.25f), item.Behavior.TurnOrder);
        Assert.Single(item.BehaviorEvents);
        Assert.Equal(NetworkBehaviorEventType.HumanRow, item.BehaviorEvents[0].Type);
        Assert.True(item.BehaviorEvents[0].Value);
    }

    private static ComponentBody CreateBody(Vector3 position) {
        return new ComponentBody {
            Position = position,
            Rotation = Quaternion.Identity,
            Velocity = Vector3.Zero
        };
    }

    private static void PushSnapshot(ComponentBody body, double time, Vector3 position, Vector3 velocity, double playbackDelay) {
        NetworkBodyInterpolation.PushSnapshot(
            body,
            positionChanged: true,
            position,
            rotationChanged: true,
            Quaternion.Identity,
            velocityChanged: true,
            velocity,
            time,
            playbackDelay);
    }
}
