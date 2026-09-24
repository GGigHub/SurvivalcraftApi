using Godot;

namespace SurvivalcraftGodot;

public static class BlockShapes
{
    public static void Rotate(int facing, ref Vector3 min, ref Vector3 max)
    {
        for (int i = 0; i < (facing & 3); i++)
        {
            float minX = min.X;
            float maxX = max.X;
            float minZ = min.Z;
            float maxZ = max.Z;
            min.X = 1f - maxZ;
            max.X = 1f - minZ;
            min.Z = minX;
            max.Z = maxX;
        }
    }

    public static Vector3 RotatePoint(int facing, Vector3 point)
    {
        for (int i = 0; i < (facing & 3); i++)
        {
            float x = point.X;
            point.X = 1f - point.Z;
            point.Z = x;
        }

        return point;
    }

    public static bool DoorOpen(byte state) => (state & 4) != 0;

    public static int DoorFacing(byte state) => (state & 3) + (DoorOpen(state) ? 1 : 0);

    public static byte ToggleDoor(byte state) => (byte)(state ^ 4);
}
