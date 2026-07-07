using System.Globalization;

namespace Survivalcraft.Test;

public readonly record struct Vector3Dto(float X, float Y, float Z) {
    public static float DistanceSquared(Vector3Dto v1, Vector3Dto v2) {
        float x = v1.X - v2.X;
        float y = v1.Y - v2.Y;
        float z = v1.Z - v2.Z;
        return x * x + y * y + z * z;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R}, {Z:R})");
}
