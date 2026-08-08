using Engine;

namespace Game;

public static class PlayerItemUsePosition {
    public const float StandingEyeHeightFactor = 0.875f;
    public const float CrouchingEyeHeightFactor = 0.45f;

    public static Vector3 Calculate(
        Vector3 position,
        Quaternion rotation,
        Vector3 boxSize,
        float targetCrouchFactor,
        float rightOffset,
        float upOffset,
        float forwardOffset) {
        Matrix matrix = Matrix.CreateFromQuaternion(rotation);
        float crouchFactor = MathUtils.Sigmoid(MathUtils.Saturate(targetCrouchFactor), 4f);
        float standingHeight = StandingEyeHeightFactor * boxSize.Y;
        float eyeHeight = MathUtils.Lerp(standingHeight, CrouchingEyeHeightFactor * standingHeight, crouchFactor);
        return position
            + matrix.Up * (eyeHeight + upOffset)
            + matrix.Right * rightOffset
            + matrix.Forward * forwardOffset;
    }

    public static Vector3 Calculate(ComponentBody body, float rightOffset, float upOffset, float forwardOffset) {
        return Calculate(
            body.Position,
            body.Rotation,
            body.BoxSize,
            body.TargetCrouchFactor,
            rightOffset,
            upOffset,
            forwardOffset
        );
    }
}
