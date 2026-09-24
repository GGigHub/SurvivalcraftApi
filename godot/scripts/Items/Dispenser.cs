using System.Collections.Generic;
using Godot;

namespace SurvivalcraftGodot;

/// <summary>
/// 发射器存一叠物品。旁边的按钮按下时，朝它的朝向弹出一件。前面是空格子就把这件放成方块。
/// </summary>
public sealed class DispenserStore
{
    readonly Dictionary<(int X, int Y, int Z), (ushort Id, int Count)> _contents = new();

    public bool TryInsert(Inventory inventory, int slot, VoxelHit hit, out string message)
    {
        message = "";
        ushort held = inventory.Counts[slot] > 0 ? inventory.Ids[slot] : BlockIds.Air;
        if (held == BlockIds.Air || held == BlockIds.Dispenser)
        {
            message = "手里没有能装进去的东西";
            return false;
        }

        var key = (hit.Block.X, hit.Block.Y, hit.Block.Z);
        if (_contents.TryGetValue(key, out (ushort Id, int Count) stack) && stack.Count > 0 && stack.Id != held)
        {
            message = "里面已经是别的东西";
            return false;
        }

        int count = stack.Count + 1;
        _contents[key] = (held, count);
        inventory.TryConsume(slot);
        message = $"装进{BlockCatalog.NameOf(held)}";
        return true;
    }

    public bool TryEject(VoxelWorld world, int x, int y, int z, out string message)
    {
        message = "";
        var key = (x, y, z);
        if (!_contents.TryGetValue(key, out (ushort Id, int Count) stack) || stack.Count <= 0)
        {
            message = "发射器是空的";
            return false;
        }

        byte facing = world.GetState(x, y, z);
        Vector3I step = Step(facing);
        int tx = x + step.X;
        int ty = y + step.Y;
        int tz = z + step.Z;
        if (world.GetBlock(tx, ty, tz) != BlockIds.Air)
        {
            message = "前面堵住了";
            return false;
        }

        if (!world.SetBlock(tx, ty, tz, stack.Id))
        {
            message = "前面堵住了";
            return false;
        }

        stack.Count--;
        if (stack.Count == 0)
        {
            _contents.Remove(key);
        }
        else
        {
            _contents[key] = stack;
        }

        message = $"弹出{BlockCatalog.NameOf(stack.Id)}";
        return true;
    }

    public void EjectNeighbors(VoxelWorld world, int x, int y, int z, out string message)
    {
        message = "";
        (int X, int Y, int Z)[] neighbors =
        {
            (x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1),
        };
        foreach ((int nx, int ny, int nz) in neighbors)
        {
            if (world.GetBlock(nx, ny, nz) != BlockIds.Dispenser)
            {
                continue;
            }

            if (TryEject(world, nx, ny, nz, out message))
            {
                return;
            }
        }
    }

    public static Vector3I Step(byte facing) => (facing & 3) switch
    {
        0 => new Vector3I(1, 0, 0),
        1 => new Vector3I(0, 0, 1),
        2 => new Vector3I(-1, 0, 0),
        _ => new Vector3I(0, 0, -1),
    };
}
