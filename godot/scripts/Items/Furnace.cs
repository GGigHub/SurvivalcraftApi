using System.Collections.Generic;

namespace SurvivalcraftGodot;

/// <summary>
/// 一格熔炉：燃料热度和燃烧时间来自方块表，产物来自需要热量的合成配方。烧完一件大约 1/0.15 秒。
/// </summary>
public sealed class Furnace
{
    public const float SmeltSpeed = 0.15f;

    public ushort InputId { get; private set; }
    public int InputCount { get; private set; }
    public ushort FuelId { get; private set; }
    public int FuelCount { get; private set; }
    public ushort OutputId { get; private set; }
    public int OutputCount { get; private set; }
    public float FireTime { get; private set; }
    public float Heat { get; private set; }
    public float Progress { get; private set; }

    public bool AddInput(ushort id, int count)
    {
        if (id == BlockIds.Air || count <= 0)
        {
            return false;
        }

        if (InputCount > 0 && InputId != id)
        {
            return false;
        }

        InputId = id;
        InputCount += count;
        return true;
    }

    public bool AddFuel(ushort id, int count)
    {
        if (count <= 0 || !OriginalBlockData.TryGet(id, out OriginalBlockRow row) || row.FuelHeat <= 0f)
        {
            return false;
        }

        if (FuelCount > 0 && FuelId != id)
        {
            return false;
        }

        FuelId = id;
        FuelCount += count;
        return true;
    }

    public ushort TakeOutput()
    {
        if (OutputCount <= 0)
        {
            return BlockIds.Air;
        }

        OutputCount--;
        ushort id = OutputId;
        if (OutputCount == 0)
        {
            OutputId = BlockIds.Air;
        }

        return id;
    }

    public void Tick(float dt, RecipeBook book)
    {
        if (FireTime > 0f)
        {
            FireTime -= dt;
            if (FireTime <= 0f)
            {
                FireTime = 0f;
                Heat = 0f;
            }
        }

        bool matched = book.TrySmelt(InputId, Heat > 0f ? Heat : PeekFuelHeat(), out int resultId, out int resultCount);
        if (!matched)
        {
            Progress = 0f;
            return;
        }

        if (FireTime <= 0f && !BurnOneFuel())
        {
            Progress = 0f;
            return;
        }

        if (!book.TrySmelt(InputId, Heat, out resultId, out resultCount))
        {
            return;
        }

        Progress += SmeltSpeed * dt;
        if (Progress < 1f)
        {
            return;
        }

        InputCount--;
        if (InputCount == 0)
        {
            InputId = BlockIds.Air;
        }

        if (OutputCount == 0)
        {
            OutputId = (ushort)resultId;
        }

        if (OutputId == resultId)
        {
            OutputCount += resultCount;
        }

        Progress = 0f;
    }

    float PeekFuelHeat()
    {
        if (FuelCount <= 0 || !OriginalBlockData.TryGet(FuelId, out OriginalBlockRow row))
        {
            return 0f;
        }

        return row.FuelHeat;
    }

    bool BurnOneFuel()
    {
        if (FuelCount <= 0 || !OriginalBlockData.TryGet(FuelId, out OriginalBlockRow row) || row.FuelHeat <= 0f)
        {
            return false;
        }

        FuelCount--;
        if (FuelCount == 0)
        {
            FuelId = BlockIds.Air;
        }

        FireTime = row.FuelTime;
        Heat = row.FuelHeat;
        return FireTime > 0f;
    }
}

public sealed class FurnaceStore
{
    readonly Dictionary<(int X, int Y, int Z), Furnace> _furnaces = new();

    public void Tick(float dt, RecipeBook? book, VoxelWorld world)
    {
        if (book == null)
        {
            return;
        }

        foreach (KeyValuePair<(int X, int Y, int Z), Furnace> pair in _furnaces)
        {
            pair.Value.Tick(dt, book);
            byte state = world.GetState(pair.Key.X, pair.Key.Y, pair.Key.Z);
            byte next = (byte)((state & 3) | (pair.Value.FireTime > 0f ? 4 : 0));
            if (next != state && world.GetBlock(pair.Key.X, pair.Key.Y, pair.Key.Z) == BlockIds.Furnace)
            {
                world.SetBlock(pair.Key.X, pair.Key.Y, pair.Key.Z, BlockIds.Furnace, next);
            }
        }
    }

    public bool TryUse(Inventory inventory, int slot, VoxelHit hit, out string message)
    {
        message = "";
        var key = (hit.Block.X, hit.Block.Y, hit.Block.Z);
        if (!_furnaces.TryGetValue(key, out Furnace? furnace))
        {
            furnace = new Furnace();
            _furnaces[key] = furnace;
        }

        ushort held = inventory.Counts[slot] > 0 ? inventory.Ids[slot] : BlockIds.Air;
        if (held == BlockIds.Air)
        {
            ushort taken = furnace.TakeOutput();
            if (taken == BlockIds.Air)
            {
                message = furnace.FireTime > 0f ? "熔炉还在烧，稍等再取" : "熔炉是空的";
                return false;
            }

            inventory.Add(taken, 1);
            message = $"取出{BlockCatalog.NameOf(taken)}";
            return true;
        }

        if (furnace.AddFuel(held, 1))
        {
            inventory.TryConsume(slot);
            message = $"放进燃料：{BlockCatalog.NameOf(held)}";
            return true;
        }

        if (furnace.AddInput(held, 1))
        {
            inventory.TryConsume(slot);
            message = furnace.FuelCount > 0 || furnace.FireTime > 0f
                ? $"放进{BlockCatalog.NameOf(held)}，熔炉开始烧"
                : $"放进{BlockCatalog.NameOf(held)}，还没有燃料";
            return true;
        }

        message = "这样东西放不进熔炉";
        return false;
    }
}
