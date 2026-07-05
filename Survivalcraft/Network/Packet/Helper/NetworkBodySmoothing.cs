using System.Collections.Generic;
using Engine;

namespace Game.Network;

public static class NetworkBodySmoothing {
    public const float PositionSharpness = 18f;
    public const float RotationSharpness = 20f;
    public const float SnapDistanceSquared = 16f;
    public const float PositionSettleDistanceSquared = 0.000001f;
    public const float RotationSettleDotEpsilon = 0.000001f;

    public struct BodySmoothingState {
        public bool Initialized;
        public bool HasPositionTarget;
        public bool HasRotationTarget;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Vector3 TargetVelocity;
    }

    public static readonly Dictionary<ComponentBody, BodySmoothingState> s_states = [];

    public static void SetTarget(
        ComponentBody body,
        bool positionChanged,
        Vector3 position,
        bool rotationChanged,
        Quaternion rotation,
        bool velocityChanged,
        Vector3 velocity,
        bool smooth) {
        if (body == null) {
            return;
        }

        if (!smooth) {
            RemoveTarget(body);
            ApplyDirect(body, positionChanged, position, rotationChanged, rotation, velocityChanged, velocity);
            return;
        }

        if (!s_states.TryGetValue(body, out BodySmoothingState state) || !state.Initialized) {
            state = new BodySmoothingState {
                Initialized = true,
                HasPositionTarget = true,
                HasRotationTarget = true,
                TargetPosition = body.Position,
                TargetRotation = body.Rotation,
                TargetVelocity = body.Velocity
            };

            ApplyDirect(body, positionChanged, position, rotationChanged, rotation, velocityChanged, velocity);
            state.TargetPosition = body.Position;
            state.TargetRotation = body.Rotation;
            state.TargetVelocity = body.Velocity;
            s_states[body] = state;
            return;
        }

        if (velocityChanged) {
            body.Velocity = velocity;
            state.TargetVelocity = body.Velocity;
        }
        if (positionChanged) {
            state.HasPositionTarget = true;
            state.TargetPosition = position;
            if ((position - body.Position).LengthSquared() > SnapDistanceSquared) {
                body.Position = position;
            }
        }
        if (rotationChanged) {
            state.HasRotationTarget = true;
            state.TargetRotation = rotation;
        }
        s_states[body] = state;
    }

    public static void Update(ComponentBody body, float dt) {
        if (body == null || !s_states.TryGetValue(body, out BodySmoothingState state)) {
            return;
        }

        if (dt <= 0f) {
            return;
        }

        if (state.HasPositionTarget) {
            Vector3 delta = state.TargetPosition - body.Position;
            if (delta.LengthSquared() > SnapDistanceSquared) {
                body.Position = state.TargetPosition;
            }
            else if (delta.LengthSquared() <= PositionSettleDistanceSquared) {
                body.Position = state.TargetPosition;
            }
            else {
                body.Position = Vector3.Lerp(body.Position, state.TargetPosition, ExponentialFactor(PositionSharpness, dt));
            }
        }

        if (state.HasRotationTarget) {
            float dot = MathF.Abs(Quaternion.Dot(body.Rotation, state.TargetRotation));
            if (1f - dot <= RotationSettleDotEpsilon) {
                body.Rotation = state.TargetRotation;
            }
            else {
                body.Rotation = Quaternion.Slerp(body.Rotation, state.TargetRotation, ExponentialFactor(RotationSharpness, dt));
            }
        }
    }

    public static void RemoveTarget(ComponentBody body) {
        if (body != null) {
            s_states.Remove(body);
        }
    }

    public static void Clear() {
        s_states.Clear();
    }

    public static void ApplyDirect(
        ComponentBody body,
        bool positionChanged,
        Vector3 position,
        bool rotationChanged,
        Quaternion rotation,
        bool velocityChanged,
        Vector3 velocity) {
        if (velocityChanged) {
            body.Velocity = velocity;
        }
        if (positionChanged) {
            body.Position = position;
        }
        if (rotationChanged) {
            body.Rotation = rotation;
        }
    }

    public static float ExponentialFactor(float sharpness, float dt) {
        return MathUtils.Saturate(1f - MathF.Exp(0f - sharpness * dt));
    }
}
