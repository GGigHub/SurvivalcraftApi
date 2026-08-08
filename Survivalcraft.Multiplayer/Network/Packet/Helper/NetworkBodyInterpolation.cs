using Engine;

namespace Game.Network;

public static class NetworkBodyInterpolation {
    public const double RemotePlayerDelay = 0.5;
    public const double RemoteBodyDelay = 0.25;
    public const double DefaultSnapshotInterval = 0.2;
    public const double MaxExtrapolationTime = 0.5;
    public const double MaxBufferedTime = 2.0;
    public const int MaxSnapshots = 150;
    public const float SnapDistanceSquared = 16f;
    public const float RemoteBodyMaxPositionLagSquared = 9f;
    public const float CatchUpSpeed = 1.15f;
    public const float MaxPlaybackStep = 0.25f;

    public struct BodySnapshot {
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
    }

    public sealed class BodyInterpolationState {
        public double PlaybackDelay;
        public double PlaybackTime;
        public bool HasPlaybackTime;
        public readonly List<BodySnapshot> Snapshots = [];
    }

    public static readonly Dictionary<ComponentBody, BodyInterpolationState> s_states = [];

    public static void PushSnapshot(
        ComponentBody body,
        bool positionChanged,
        Vector3 position,
        bool rotationChanged,
        Quaternion rotation,
        bool velocityChanged,
        Vector3 velocity,
        double snapshotTime,
        double playbackDelay,
        float maxPositionLagSquared = float.MaxValue) {
        if (body == null || (!positionChanged && !rotationChanged && !velocityChanged)) {
            return;
        }

        BodyInterpolationState state = GetOrCreateState(body, playbackDelay);
        BodySnapshot snapshot = state.Snapshots.Count > 0
            ? state.Snapshots[^1]
            : new BodySnapshot {
                Position = body.Position,
                Rotation = body.Rotation,
                Velocity = body.Velocity
            };

        snapshot.Time = snapshotTime;
        if (positionChanged) {
            snapshot.Position = position;
        }
        if (rotationChanged) {
            snapshot.Rotation = rotation;
        }
        if (velocityChanged) {
            snapshot.Velocity = velocity;
        }

        if (state.Snapshots.Count > 0) {
            BodySnapshot latest = state.Snapshots[^1];
            if (snapshot.Time < latest.Time) {
                return;
            }
            if (snapshot.Time == latest.Time) {
                state.Snapshots[^1] = snapshot;
                return;
            }
            bool authoritativeDiscontinuity =
                (snapshot.Position - latest.Position).LengthSquared() > SnapDistanceSquared;
            bool playbackDriftExceeded =
                maxPositionLagSquared < float.MaxValue
                && (snapshot.Position - body.Position).LengthSquared() > maxPositionLagSquared;
            if (authoritativeDiscontinuity || playbackDriftExceeded) {
                state.Snapshots.Clear();
                state.PlaybackTime = snapshot.Time;
                state.HasPlaybackTime = true;
                ApplySnapshot(body, snapshot);
            }
        }
        else {
            state.PlaybackTime = snapshot.Time;
            state.HasPlaybackTime = true;
            ApplySnapshot(body, snapshot);
        }

        state.Snapshots.Add(snapshot);
        PruneSnapshots(state, snapshot.Time);
    }

    public static void Update(ComponentBody body, float dt) {
        if (body == null || !s_states.TryGetValue(body, out BodyInterpolationState state) || state.Snapshots.Count == 0) {
            return;
        }

        double playbackTime = AdvancePlaybackTime(state, dt);
        while (state.Snapshots.Count >= 2 && state.Snapshots[1].Time <= playbackTime) {
            state.Snapshots.RemoveAt(0);
        }

        if (state.Snapshots.Count >= 2) {
            BodySnapshot a = state.Snapshots[0];
            BodySnapshot b = state.Snapshots[1];
            if (playbackTime < a.Time) {
                return;
            }

            double duration = b.Time - a.Time;
            float factor = duration > 0.0
                ? MathUtils.Saturate((float)((playbackTime - a.Time) / duration))
                : 1f;
            ApplySnapshot(body, Interpolate(a, b, factor));
            return;
        }

        BodySnapshot latest = state.Snapshots[0];
        double extrapolationTime = MathUtils.Max(0.0, playbackTime - latest.Time);
        if (extrapolationTime <= MaxExtrapolationTime) {
            latest.Position += latest.Velocity * (float)extrapolationTime;
        }
        ApplySnapshot(body, latest);
    }

    public static double AdvancePlaybackTime(BodyInterpolationState state, float dt) {
        BodySnapshot first = state.Snapshots[0];
        BodySnapshot latest = state.Snapshots[^1];
        if (!state.HasPlaybackTime) {
            state.PlaybackTime = first.Time;
            state.HasPlaybackTime = true;
        }

        double bufferedDuration = latest.Time - first.Time;
        double targetTime = bufferedDuration > state.PlaybackDelay
            ? latest.Time - state.PlaybackDelay
            : latest.Time;
        if (state.PlaybackTime < first.Time) {
            state.PlaybackTime = first.Time;
        }
        if (state.PlaybackTime < targetTime) {
            double lag = targetTime - state.PlaybackTime;
            float speed = lag > DefaultSnapshotInterval ? CatchUpSpeed : 1f;
            state.PlaybackTime = MathUtils.Min(
                targetTime,
                state.PlaybackTime + MathUtils.Min(dt * speed, MaxPlaybackStep));
        }
        return state.PlaybackTime;
    }

    public static void RemoveTarget(ComponentBody body) {
        if (body != null) {
            s_states.Remove(body);
        }
    }

    public static void Clear() {
        s_states.Clear();
    }

    public static BodyInterpolationState GetOrCreateState(ComponentBody body, double playbackDelay) {
        if (!s_states.TryGetValue(body, out BodyInterpolationState state)) {
            state = new BodyInterpolationState();
            s_states[body] = state;
        }
        state.PlaybackDelay = playbackDelay;
        return state;
    }

    public static BodySnapshot Interpolate(BodySnapshot a, BodySnapshot b, float factor) {
        if ((b.Position - a.Position).LengthSquared() > SnapDistanceSquared) {
            return factor < 1f ? a : b;
        }
        return new BodySnapshot {
            Time = MathUtils.Lerp(a.Time, b.Time, factor),
            Position = Vector3.Lerp(a.Position, b.Position, factor),
            Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, factor),
            Velocity = Vector3.Lerp(a.Velocity, b.Velocity, factor)
        };
    }

    public static void ApplySnapshot(ComponentBody body, BodySnapshot snapshot) {
        body.Position = snapshot.Position;
        body.Rotation = snapshot.Rotation;
        body.Velocity = snapshot.Velocity;
    }

    public static void PruneSnapshots(BodyInterpolationState state, double now) {
        while (state.Snapshots.Count > MaxSnapshots) {
            state.Snapshots.RemoveAt(0);
        }
        while (state.Snapshots.Count > 2 && state.Snapshots[0].Time < now - MaxBufferedTime) {
            state.Snapshots.RemoveAt(0);
        }
    }
}
