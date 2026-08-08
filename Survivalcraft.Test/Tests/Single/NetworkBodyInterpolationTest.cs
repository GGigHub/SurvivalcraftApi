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

        Assert.InRange(observerBody.Position.X, 0.6f, 0.9f);
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

        Assert.InRange(clientBody.Position.Z, -6.4f, -5.6f);
        Assert.Equal(20f, clientBody.Position.X);
        Assert.True(clientBody.Position.Z < 0f, $"Expected smoothed playback before latest server position. Actual={clientBody.Position}.");
    }

    [Fact]
    public void FastRemoteBodySnapsBeforePlaybackDriftsBeyondSpatialBudget() {
        ComponentBody clientBody = CreateBody(new Vector3(0f, 70f, 0f));

        PushRemoteBodySnapshot(clientBody, 0.0, new Vector3(0f, 70f, 0f));
        PushRemoteBodySnapshot(clientBody, 0.1, new Vector3(1f, 70f, 0f));
        PushRemoteBodySnapshot(clientBody, 0.2, new Vector3(2f, 70f, 0f));
        PushRemoteBodySnapshot(clientBody, 0.3, new Vector3(3f, 70f, 0f));

        Assert.Equal(0f, clientBody.Position.X);

        PushRemoteBodySnapshot(clientBody, 0.4, new Vector3(4f, 70f, 0f));

        Assert.Equal(4f, clientBody.Position.X);
        Assert.Single(NetworkBodyInterpolation.s_states[clientBody].Snapshots);
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

    private static void PushRemoteBodySnapshot(ComponentBody body, double time, Vector3 position) {
        NetworkBodyInterpolation.PushSnapshot(
            body,
            positionChanged: true,
            position,
            rotationChanged: true,
            Quaternion.Identity,
            velocityChanged: true,
            new Vector3(10f, 0f, 0f),
            time,
            NetworkBodyInterpolation.RemoteBodyDelay,
            NetworkBodyInterpolation.RemoteBodyMaxPositionLagSquared);
    }
}
