using System.Globalization;

namespace Survivalcraft.Test;

public readonly record struct Vector2Dto(float X, float Y) {
    public static float DistanceSquared(Vector2Dto v1, Vector2Dto v2) {
        float x = v1.X - v2.X;
        float y = v1.Y - v2.Y;
        return x * x + y * y;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R})");
}

public readonly record struct Vector3Dto(float X, float Y, float Z) {
    public static float DistanceSquared(Vector3Dto v1, Vector3Dto v2) {
        float x = v1.X - v2.X;
        float y = v1.Y - v2.Y;
        float z = v1.Z - v2.Z;
        return x * x + y * y + z * z;
    }

    public static float HorizontalDistanceSquared(Vector3Dto v1, Vector3Dto v2) {
        float x = v1.X - v2.X;
        float z = v1.Z - v2.Z;
        return x * x + z * z;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R}, {Z:R})");
}
