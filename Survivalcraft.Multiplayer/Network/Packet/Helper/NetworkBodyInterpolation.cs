using Engine;

namespace Game.Network;

public static class NetworkBodyInterpolation {
    public const double RemoteBodyDelay = 0.6;
    public const double RemotePlayerDelay = RemoteBodyDelay;
    public const double DefaultSnapshotInterval = 0.2;
    public const double MaxExtrapolationTime = 0.6;
    public const double MaxServerPredictionTime = 0.35;
    public const double MaxBufferedTime = 2.0;
    public const int MaxSnapshots = 150;
    public const float SnapDistanceSquared = 16f;
    public const float ServerCorrectionRate = 12f;
    public const float CatchUpSpeed = 1.15f;
    public const float MaxPlaybackStep = 0.25f;

    public struct BodySnapshot {
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
    }

    public readonly struct BehaviorSnapshot(double time, int sequence, NetworkBehaviorState state) {
        public double Time { get; } = time;
        public int Sequence { get; } = sequence;
        public NetworkBehaviorState State { get; } = state?.Clone();
    }

    public sealed class BodyInterpolationState {
        public double PlaybackDelay;
        public double PlaybackTime;
        public bool HasPlaybackTime;
        public readonly List<BodySnapshot> Snapshots = [];
        public double LatestTimelineTime;
        public bool HasLatestTimelineTime;
        public double FirstTimelineTime;
        public bool HasFirstTimelineTime;
        public readonly List<BehaviorSnapshot> BehaviorSnapshots = [];
        public readonly List<NetworkBehaviorEvent> BehaviorEvents = [];
        public double LastBehaviorStateTime;
        public int LastBehaviorStateSequence;
        public bool HasLastBehaviorState;
        public double LastBehaviorEventTime;
        public int LastBehaviorEventSequence;
        public bool HasLastBehaviorEvent;

        // Server-side movement uses the latest received state directly and
        // predicts forward from it. These fields are intentionally separate
        // from the client-side delayed snapshot queue below.
        public bool HasServerTarget;
        public double ServerTargetReceiveTime;
        public Vector3 ServerTargetPosition;
        public Quaternion ServerTargetRotation;
        public Vector3 ServerTargetVelocity;
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
        NetworkBehaviorState behaviorState = null,
        IReadOnlyList<NetworkBehaviorEvent> behaviorEvents = null,
        int sequence = 0) {
        if (body == null) {
            return;
        }

        BodyInterpolationState state = GetOrCreateState(body, playbackDelay);
        RegisterTimelineTime(state, snapshotTime);
        if (behaviorState != null) {
            QueueBehaviorState(state, snapshotTime, sequence, behaviorState);
        }
        if (behaviorEvents != null) {
            QueueBehaviorEvents(state, behaviorEvents);
        }
        if (!positionChanged && !rotationChanged && !velocityChanged) {
            return;
        }

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
            if ((snapshot.Position - latest.Position).LengthSquared() > SnapDistanceSquared) {
                state.Snapshots.Clear();
                state.PlaybackTime = snapshot.Time;
                state.HasPlaybackTime = true;
                ApplySnapshot(body, snapshot);
            }
        }
        else {
            // Apply the first authoritative state immediately so a newly
            // created entity does not remain at its spawn position while the
            // presentation timeline is being primed. Playback itself starts
            // only after enough timeline data has been buffered.
            ApplySnapshot(body, snapshot);
        }

        state.Snapshots.Add(snapshot);
        PruneSnapshots(state, snapshot.Time);
    }

    public static void Update(ComponentBody body, float dt) {
        if (body == null
            || !s_states.TryGetValue(body, out BodyInterpolationState state)
            || !HasPresentationData(state)) {
            return;
        }

        double playbackTime = AdvancePlaybackTime(state, dt);
        if (!state.HasPlaybackTime) {
            // The presentation clock is intentionally held during startup
            // until a complete interpolation delay is available.
            return;
        }
        while (state.Snapshots.Count >= 2 && state.Snapshots[1].Time <= playbackTime) {
            state.Snapshots.RemoveAt(0);
        }

        if (state.Snapshots.Count >= 2) {
            BodySnapshot a = state.Snapshots[0];
            BodySnapshot b = state.Snapshots[1];
            if (playbackTime >= a.Time) {
                double duration = b.Time - a.Time;
                float factor = duration > 0.0
                    ? MathUtils.Saturate((float)((playbackTime - a.Time) / duration))
                    : 1f;
                ApplySnapshot(body, Interpolate(a, b, factor));
            }
        }
        else if (state.Snapshots.Count == 1) {
            BodySnapshot latest = state.Snapshots[0];
            double extrapolationTime = MathUtils.Max(0.0, playbackTime - latest.Time);
            if (extrapolationTime <= MaxExtrapolationTime) {
                latest.Position += latest.Velocity * (float)extrapolationTime;
            }
            ApplySnapshot(body, latest);
        }

        ApplyBehaviorSnapshots(body, state, playbackTime);
        ApplyBehaviorEvents(body, state, playbackTime);
    }

    public static void QueueBehaviorState(
        ComponentBody body,
        double stateTime,
        int sequence,
        NetworkBehaviorState state) {
        if (body == null || state == null) {
            return;
        }
        BodyInterpolationState interpolationState = GetOrCreateState(body, RemoteBodyDelay);
        RegisterTimelineTime(interpolationState, stateTime);
        QueueBehaviorState(interpolationState, stateTime, sequence, state);
    }

    public static void QueueBehaviorEvents(
        ComponentBody body,
        IReadOnlyList<NetworkBehaviorEvent> behaviorEvents) {
        if (body == null || behaviorEvents == null || behaviorEvents.Count == 0) {
            return;
        }
        BodyInterpolationState state = GetOrCreateState(body, RemoteBodyDelay);
        QueueBehaviorEvents(state, behaviorEvents);
    }

    public static void PushServerSnapshot(
        ComponentBody body,
        bool positionChanged,
        Vector3 position,
        bool rotationChanged,
        Quaternion rotation,
        bool velocityChanged,
        Vector3 velocity) {
        if (body == null) {
            return;
        }

        BodyInterpolationState state = GetOrCreateState(body, 0.0);
        if (!state.HasServerTarget) {
            state.ServerTargetPosition = body.Position;
            state.ServerTargetRotation = body.Rotation;
            state.ServerTargetVelocity = body.Velocity;
            state.HasServerTarget = true;
        }
        if (positionChanged) {
            state.ServerTargetPosition = position;
        }
        if (rotationChanged) {
            state.ServerTargetRotation = rotation;
        }
        if (velocityChanged) {
            state.ServerTargetVelocity = velocity;
        }
        state.ServerTargetReceiveTime = Engine.Time.RealTime;
    }

    public static void UpdateServer(ComponentBody body, float dt) {
        if (body == null
            || !s_states.TryGetValue(body, out BodyInterpolationState state)
            || !state.HasServerTarget) {
            return;
        }

        double elapsed = MathUtils.Max(0.0, Engine.Time.RealTime - state.ServerTargetReceiveTime);
        float predictionTime = (float)MathUtils.Min(elapsed, MaxServerPredictionTime);
        Vector3 predictedPosition = state.ServerTargetPosition + state.ServerTargetVelocity * predictionTime;
        Vector3 error = predictedPosition - body.Position;
        float factor = 1f - MathF.Exp(-ServerCorrectionRate * dt);

        if (error.LengthSquared() > SnapDistanceSquared) {
            body.Position = predictedPosition;
            body.Rotation = state.ServerTargetRotation;
            body.Velocity = state.ServerTargetVelocity;
            return;
        }

        body.Position += error * factor;
        body.Rotation = Quaternion.Slerp(body.Rotation, state.ServerTargetRotation, factor);
        Vector3 desiredVelocity = elapsed <= MaxServerPredictionTime
            ? state.ServerTargetVelocity
            : Vector3.Zero;
        body.Velocity = Vector3.Lerp(body.Velocity, desiredVelocity, factor);
    }

    public static double AdvancePlaybackTime(BodyInterpolationState state, float dt) {
        if (!state.HasLatestTimelineTime) {
            return state.PlaybackTime;
        }

        double firstTime = state.Snapshots.Count > 0
            ? state.Snapshots[0].Time
            : state.FirstTimelineTime;
        if (!state.HasPlaybackTime) {
            double bufferedDuration = state.LatestTimelineTime - firstTime;
            if (bufferedDuration < state.PlaybackDelay) {
                return state.PlaybackTime;
            }

            // Start from the delayed presentation point once the initial
            // buffer is ready. This prevents a live stream from following the
            // newest snapshot first and only becoming delayed later.
            state.PlaybackTime = state.LatestTimelineTime - state.PlaybackDelay;
            state.HasPlaybackTime = true;
        }

        if (state.PlaybackTime < firstTime && state.Snapshots.Count > 0) {
            state.PlaybackTime = firstTime;
        }

        // Once playback has started, the amount of available future data is
        // measured from the playback cursor. Using the oldest retained
        // snapshot here becomes incorrect after the queue has been pruned.
        double targetTime = state.LatestTimelineTime - state.PlaybackDelay;
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

    private static bool HasPresentationData(BodyInterpolationState state) {
        return state.Snapshots.Count > 0
            || state.BehaviorSnapshots.Count > 0
            || state.BehaviorEvents.Count > 0;
    }

    private static void RegisterTimelineTime(BodyInterpolationState state, double time) {
        if (!state.HasLatestTimelineTime || time > state.LatestTimelineTime) {
            state.LatestTimelineTime = time;
            state.HasLatestTimelineTime = true;
        }
        if (!state.HasFirstTimelineTime || time < state.FirstTimelineTime) {
            state.FirstTimelineTime = time;
            state.HasFirstTimelineTime = true;
        }
    }

    private static void QueueBehaviorState(
        BodyInterpolationState state,
        double stateTime,
        int sequence,
        NetworkBehaviorState behaviorState) {
        if (state.HasLastBehaviorState
            && CompareKey(stateTime, sequence, state.LastBehaviorStateTime, state.LastBehaviorStateSequence) < 0) {
            return;
        }

        int index = state.BehaviorSnapshots.Count;
        while (index > 0
            && CompareKey(
                state.BehaviorSnapshots[index - 1].Time,
                state.BehaviorSnapshots[index - 1].Sequence,
                stateTime,
                sequence) > 0) {
            index--;
        }
        state.BehaviorSnapshots.Insert(index, new BehaviorSnapshot(stateTime, sequence, behaviorState));
    }

    private static void QueueBehaviorEvents(
        BodyInterpolationState state,
        IReadOnlyList<NetworkBehaviorEvent> behaviorEvents) {
        foreach (NetworkBehaviorEvent behaviorEvent in behaviorEvents) {
            RegisterTimelineTime(state, behaviorEvent.Time);
            if (state.HasLastBehaviorEvent
                && CompareKey(
                    behaviorEvent.Time,
                    behaviorEvent.Sequence,
                    state.LastBehaviorEventTime,
                    state.LastBehaviorEventSequence) < 0) {
                continue;
            }

            int index = state.BehaviorEvents.Count;
            while (index > 0
                && CompareKey(
                    state.BehaviorEvents[index - 1].Time,
                    state.BehaviorEvents[index - 1].Sequence,
                    behaviorEvent.Time,
                    behaviorEvent.Sequence) > 0) {
                index--;
            }
            state.BehaviorEvents.Insert(index, behaviorEvent);
        }
    }

    private static void ApplyBehaviorSnapshots(
        ComponentBody body,
        BodyInterpolationState state,
        double playbackTime) {
        while (state.BehaviorSnapshots.Count > 0
            && state.BehaviorSnapshots[0].Time <= playbackTime) {
            BehaviorSnapshot snapshot = state.BehaviorSnapshots[0];
            state.BehaviorSnapshots.RemoveAt(0);
            if (state.HasLastBehaviorState
                && CompareKey(snapshot.Time, snapshot.Sequence, state.LastBehaviorStateTime, state.LastBehaviorStateSequence) < 0) {
                continue;
            }
            NetworkBehaviorSyncHelpers.ApplyState(body, snapshot.State);
            state.LastBehaviorStateTime = snapshot.Time;
            state.LastBehaviorStateSequence = snapshot.Sequence;
            state.HasLastBehaviorState = true;
        }
    }

    private static void ApplyBehaviorEvents(
        ComponentBody body,
        BodyInterpolationState state,
        double playbackTime) {
        while (state.BehaviorEvents.Count > 0
            && state.BehaviorEvents[0].Time <= playbackTime) {
            NetworkBehaviorEvent behaviorEvent = state.BehaviorEvents[0];
            state.BehaviorEvents.RemoveAt(0);
            if (state.HasLastBehaviorEvent
                && CompareKey(
                    behaviorEvent.Time,
                    behaviorEvent.Sequence,
                    state.LastBehaviorEventTime,
                    state.LastBehaviorEventSequence) < 0) {
                continue;
            }
            NetworkBehaviorSyncHelpers.ApplyEvent(body, behaviorEvent);
            state.LastBehaviorEventTime = behaviorEvent.Time;
            state.LastBehaviorEventSequence = behaviorEvent.Sequence;
            state.HasLastBehaviorEvent = true;
        }
    }

    private static int CompareKey(double leftTime, int leftSequence, double rightTime, int rightSequence) {
        int timeResult = leftTime.CompareTo(rightTime);
        return timeResult != 0 ? timeResult : leftSequence.CompareTo(rightSequence);
    }
}
