using System.Collections.Generic;

namespace SurvivalcraftGodot;

public sealed class ChestStore
{
    const int Slots = 9;
    readonly Dictionary<long, ushort[]> _ids = new();
    readonly Dictionary<long, int[]> _counts = new();

    public void Deposit(int x, int y, int z, ushort id, int count)
    {
        if (id == BlockIds.Air || count <= 0)
        {
            return;
        }

        long key = Key(x, y, z);
        Ensure(key);
        ushort[] ids = _ids[key];
        int[] counts = _counts[key];
        for (int i = 0; i < Slots && count > 0; i++)
        {
            if (ids[i] == id && counts[i] < Inventory.StackLimit)
            {
                int room = Inventory.StackLimit - counts[i];
                int move = count < room ? count : room;
                counts[i] += move;
                count -= move;
            }
        }

        for (int i = 0; i < Slots && count > 0; i++)
        {
            if (counts[i] != 0)
            {
                continue;
            }

            int move = count < Inventory.StackLimit ? count : Inventory.StackLimit;
            ids[i] = id;
            counts[i] = move;
            count -= move;
        }
    }

    public ushort TakeOne(int x, int y, int z)
    {
        long key = Key(x, y, z);
        if (!_ids.ContainsKey(key))
        {
            return BlockIds.Air;
        }

        ushort[] ids = _ids[key];
        int[] counts = _counts[key];
        for (int i = 0; i < Slots; i++)
        {
            if (counts[i] <= 0)
            {
                continue;
            }

            ushort id = ids[i];
            counts[i]--;
            if (counts[i] == 0)
            {
                ids[i] = BlockIds.Air;
            }

            return id;
        }

        return BlockIds.Air;
    }

    public int CountOf(int x, int y, int z, ushort id)
    {
        long key = Key(x, y, z);
        if (!_counts.ContainsKey(key))
        {
            return 0;
        }

        int total = 0;
        ushort[] ids = _ids[key];
        int[] counts = _counts[key];
        for (int i = 0; i < Slots; i++)
        {
            if (ids[i] == id)
            {
                total += counts[i];
            }
        }

        return total;
    }

    void Ensure(long key)
    {
        if (_ids.ContainsKey(key))
        {
            return;
        }

        _ids[key] = new ushort[Slots];
        _counts[key] = new int[Slots];
    }

    static long Key(int x, int y, int z) => ((long)x << 42) ^ ((long)y << 21) ^ (uint)z;
}
