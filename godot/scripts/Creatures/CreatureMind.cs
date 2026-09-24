using Godot;

namespace SurvivalcraftGodot;

public static class CreatureMind
{
    public static Vector3 StepToward(VoxelWorld world, Vector3 from, Vector3 target, float distance)
    {
        float dx = target.X - from.X;
        float dz = target.Z - from.Z;
        float len = System.MathF.Sqrt((dx * dx) + (dz * dz));
        if (len < 0.05f)
        {
            return from;
        }

        float scale = distance / len;
        int x = Floor(from.X + (dx * scale));
        int z = Floor(from.Z + (dz * scale));
        int y = Floor(from.Y);
        if (BlockCatalog.Collides(world.GetBlock(x, y, z)))
        {
            y++;
        }
        else if (!BlockCatalog.Collides(world.GetBlock(x, y - 1, z)))
        {
            y--;
        }

        if (BlockCatalog.Collides(world.GetBlock(x, y, z)) || !BlockCatalog.Collides(world.GetBlock(x, y - 1, z)))
        {
            return from;
        }

        return new Vector3(x + 0.5f, y, z + 0.5f);
    }

    public static Vector3 PushOut(Vector3 feet, Vector3 creature, float minDistance)
    {
        float dx = feet.X - creature.X;
        float dz = feet.Z - creature.Z;
        float dist = System.MathF.Sqrt((dx * dx) + (dz * dz));
        if (dist < 0.0001f)
        {
            return new Vector3(feet.X + minDistance, feet.Y, feet.Z);
        }

        if (dist >= minDistance)
        {
            return feet;
        }

        float push = (minDistance - dist) / dist;
        return new Vector3(feet.X + (dx * push), feet.Y, feet.Z + (dz * push));
    }

    static int Floor(float value) => (int)System.MathF.Floor(value);
}
