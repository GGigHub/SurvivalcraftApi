using Engine;

namespace Game.Network;

public static class NetworkBodySyncHelpers {
    public const float PositionEpsilonSquared = 0.0001f;
    public const float VelocityEpsilonSquared = 0.0001f;
    public const float LookAnglesEpsilonSquared = 0.000001f;
    public const float RotationDotEpsilon = 0.000001f;

    public static bool ShouldBeNetworkControlled(ComponentBody body) {
        if (body == null || (!NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning)) {
            return false;
        }

        ComponentPlayer componentPlayer = body.Entity.FindComponent<ComponentPlayer>();
        bool isMainPlayer = componentPlayer?.PlayerData?.IsMainPlayer == true;

        bool hasNonMainChildPlayer = false;
        for (int i = 0; i < body.ChildBodies.Count; i++) {
            ComponentPlayer childPlayer = body.ChildBodies[i].Entity.FindComponent<ComponentPlayer>();
            if (childPlayer?.PlayerData?.IsMainPlayer == false) {
                hasNonMainChildPlayer = true;
                break;
            }
        }

        if (NetworkManager.IsClientRunning) {
            return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
                || hasNonMainChildPlayer;
        }

        return (componentPlayer != null && !isMainPlayer && body.ParentBody == null)
            || (componentPlayer == null && hasNonMainChildPlayer);
    }

    public static bool HasVector3Changed(Vector3 current, Vector3 previous, float epsilonSquared) {
        return (current - previous).LengthSquared() > epsilonSquared;
    }

    public static bool HasVector2Changed(Vector2 current, Vector2 previous, float epsilonSquared) {
        return (current - previous).LengthSquared() > epsilonSquared;
    }

    public static bool HasRotationChanged(Quaternion current, Quaternion previous) {
        return 1f - MathF.Abs(Quaternion.Dot(current, previous)) > RotationDotEpsilon;
    }
}
