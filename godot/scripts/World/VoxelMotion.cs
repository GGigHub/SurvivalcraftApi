using Godot;
using System;

namespace SurvivalcraftGodot;

public readonly struct VoxelHit
{
    public bool Hit { get; init; }
    public Vector3I Block { get; init; }
    public Vector3I Previous { get; init; }
}

public static class VoxelMotion
{
    public const float Radius = 0.3f;
    public const float Height = 1.75f;

    public static bool Overlaps(VoxelWorld world, Vector3 feet)
    {
        int x0 = Mathf.FloorToInt(feet.X - Radius);
        int x1 = Mathf.FloorToInt(feet.X + Radius - 0.001f);
        int y0 = Mathf.FloorToInt(feet.Y + 0.001f);
        int y1 = Mathf.FloorToInt(feet.Y + Height - 0.001f);
        int z0 = Mathf.FloorToInt(feet.Z - Radius);
        int z1 = Mathf.FloorToInt(feet.Z + Radius - 0.001f);
        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (OverlapsCell(world.GetBlock(x, y, z), world.GetState(x, y, z), x, y, z, feet))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static void Move(VoxelWorld world, ref Vector3 feet, Vector3 delta, out bool grounded)
    {
        grounded = false;
        Vector3 before = feet;
        bool blocked = false;
        if (MoveAxis(world, ref feet, new Vector3(delta.X, 0, 0)))
        {
            blocked = true;
        }

        if (MoveAxis(world, ref feet, new Vector3(0, 0, delta.Z)))
        {
            blocked = true;
        }

        if (blocked && delta.Y <= 0f)
        {
            const float step = 0.56f;
            Vector3 raised = before;
            if (!MoveAxis(world, ref raised, new Vector3(0, step, 0)))
            {
                Vector3 stepped = raised;
                bool stillBlocked = false;
                if (MoveAxis(world, ref stepped, new Vector3(delta.X, 0, 0)))
                {
                    stillBlocked = true;
                }

                if (MoveAxis(world, ref stepped, new Vector3(0, 0, delta.Z)))
                {
                    stillBlocked = true;
                }

                if (!stillBlocked)
                {
                    feet = stepped;
                    grounded = true;
                }
            }
        }

        if (MoveAxis(world, ref feet, new Vector3(0, delta.Y, 0)) && delta.Y < 0)
        {
            grounded = true;
        }
    }

    public static bool OverlapsCell(ushort id, byte state, int x, int y, int z, Vector3 feet)
    {
        if (!BlockCatalog.Collides(id))
        {
            return false;
        }

        BlockShape shape = BlockCatalog.ShapeOf(id);
        if (shape == BlockShape.Stairs)
        {
            return HitsLocal(feet, x, y, z, state, Vector3.Zero, new Vector3(1, 0.5f, 1))
                || HitsLocal(feet, x, y, z, state, new Vector3(0.5f, 0.5f, 0), Vector3.One);
        }

        if (shape == BlockShape.Door)
        {
            if (BlockShapes.DoorOpen(state))
            {
                return HitsLocal(feet, x, y, z, state & 3, Vector3.Zero, new Vector3(0.16f, 1f, 1f));
            }

            return HitsLocal(feet, x, y, z, state & 3, new Vector3(0f, 0f, 0.42f), new Vector3(1f, 1f, 0.58f));
        }

        if (shape == BlockShape.Trapdoor)
        {
            return HitsLocal(feet, x, y, z, 0, new Vector3(0, 0.84f, 0), Vector3.One);
        }

        if (shape == BlockShape.Slab)
        {
            return HitsLocal(feet, x, y, z, 0, Vector3.Zero, new Vector3(1, 0.5f, 1));
        }

        return HitsLocal(feet, x, y, z, 0, Vector3.Zero, Vector3.One);
    }

    static bool HitsLocal(Vector3 feet, int x, int y, int z, int facing, Vector3 localMin, Vector3 localMax)
    {
        BlockShapes.Rotate(facing, ref localMin, ref localMax);
        Vector3 min = new(x + localMin.X, y + localMin.Y, z + localMin.Z);
        Vector3 max = new(x + localMax.X, y + localMax.Y, z + localMax.Z);
        float x0 = feet.X - Radius;
        float x1 = feet.X + Radius;
        float y0 = feet.Y + 0.001f;
        float y1 = feet.Y + Height - 0.001f;
        float z0 = feet.Z - Radius;
        float z1 = feet.Z + Radius;
        return x1 > min.X && x0 < max.X && y1 > min.Y && y0 < max.Y && z1 > min.Z && z0 < max.Z;
    }

    public static VoxelHit Raycast(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance, bool placing = false)
    {
        direction = direction.Normalized();
        if (direction.LengthSquared() < 0.001f)
        {
            return default;
        }

        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);
        int stepX = direction.X > 0 ? 1 : direction.X < 0 ? -1 : 0;
        int stepY = direction.Y > 0 ? 1 : direction.Y < 0 ? -1 : 0;
        int stepZ = direction.Z > 0 ? 1 : direction.Z < 0 ? -1 : 0;
        float tDeltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.X);
        float tDeltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Y);
        float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Z);
        float tMaxX = stepX == 0 ? float.PositiveInfinity : (stepX > 0 ? x + 1 - origin.X : origin.X - x) * tDeltaX;
        float tMaxY = stepY == 0 ? float.PositiveInfinity : (stepY > 0 ? y + 1 - origin.Y : origin.Y - y) * tDeltaY;
        float tMaxZ = stepZ == 0 ? float.PositiveInfinity : (stepZ > 0 ? z + 1 - origin.Z : origin.Z - z) * tDeltaZ;
        var previous = new Vector3I(x, y, z);

        for (int i = 0; i < 48; i++)
        {
            ushort id = world.GetBlock(x, y, z);
            if (id != BlockIds.Air && BlockCatalog.StopsRay(id, placing) && HitsShape(id, origin, direction, x, y, z))
            {
                return new VoxelHit
                {
                    Hit = true,
                    Block = new Vector3I(x, y, z),
                    Previous = previous,
                };
            }

            previous = new Vector3I(x, y, z);
            float traveled = MathF.Min(tMaxX, MathF.Min(tMaxY, tMaxZ));
            if (traveled > maxDistance)
            {
                break;
            }

            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxZ)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                z += stepZ;
                tMaxZ += tDeltaZ;
            }
        }

        return default;
    }

    static bool HitsShape(ushort id, Vector3 origin, Vector3 direction, int x, int y, int z)
    {
        if (BlockCatalog.ShapeOf(id) != BlockShape.Slab || direction.Y >= -0.0001f)
        {
            return true;
        }

        float t = (y + 0.5f - origin.Y) / direction.Y;
        if (t < 0f)
        {
            return origin.Y - y <= 0.5f;
        }

        Vector3 point = origin + direction * t;
        return point.X >= x && point.X <= x + 1f && point.Z >= z && point.Z <= z + 1f;
    }

    static bool MoveAxis(VoxelWorld world, ref Vector3 feet, Vector3 delta)
    {
        if (delta == Vector3.Zero)
        {
            return false;
        }

        feet += delta;
        if (!Overlaps(world, feet))
        {
            return false;
        }

        feet -= delta;
        return true;
    }
}
