namespace SurvivalcraftGodot;

public sealed class Inventory
{
    public const int SlotCount = 36;
    public const int StackLimit = 40;

    public ushort[] Ids { get; } = new ushort[SlotCount];
    public int[] Counts { get; } = new int[SlotCount];
    public int[] Wear { get; } = new int[SlotCount];

    public void GiveStart()
    {
        SetSlot(0, BlockIds.Planks, 32);
        SetSlot(1, BlockIds.Slab, 16);
        SetSlot(2, BlockIds.Torch, 16);
        SetSlot(16, BlockIds.Plant, 16);
        SetSlot(3, BlockIds.Door, 8);
        SetSlot(4, BlockIds.Dispenser, 4);
        SetSlot(20, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Piston, 4);
        SetSlot(5, BlockIds.Furnace, 4);
        SetSlot(6, BlockIds.Sand, 16);
        SetSlot(18, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Wire, 32);
        SetSlot(7, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Button, 8);
        SetSlot(8, BlockIds.Air, 0);
        SetSlot(19, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.LightBulb, 8);
        SetSlot(9, BlockIds.CopperPickaxe, 1);
        SetSlot(21, BlockIds.BucketWater, 1);
        SetSlot(14, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Piston, 4);
        SetSlot(15, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.SmallGunpowderKeg, 4);
        SetSlot(10, BlockIds.Bucket, 1);
        SetSlot(11, BlockIds.Meat, 4);
        SetSlot(12, BlockIds.Trapdoor, 8);
        SetSlot(13, BlockIds.Carpet, 8);
    }

    public void SetSlot(int slot, ushort id, int count)
    {
        Ids[slot] = count > 0 ? id : BlockIds.Air;
        Counts[slot] = count > 0 ? count : 0;
        Wear[slot] = 0;
    }

    public bool TryConsume(int slot)
    {
        if (slot < 0 || slot >= SlotCount || Counts[slot] <= 0)
        {
            return false;
        }

        Counts[slot]--;
        if (Counts[slot] == 0)
        {
            Ids[slot] = BlockIds.Air;
        }

        return true;
    }

    public bool ApplyWear(int slot, out string broken)
    {
        broken = "";
        if (slot < 0 || slot >= SlotCount || Counts[slot] <= 0)
        {
            return false;
        }

        if (!OriginalBlockData.TryGet(Ids[slot], out OriginalBlockRow row) || row.Durability <= 0)
        {
            return false;
        }

        Wear[slot]++;
        if (Wear[slot] < row.Durability)
        {
            return false;
        }

        broken = $"{BlockCatalog.NameOf(Ids[slot])}坏了";
        SetSlot(slot, BlockIds.Air, 0);
        return true;
    }

    public void Add(ushort id, int count)
    {
        if (id == BlockIds.Air || count <= 0)
        {
            return;
        }

        for (int i = 0; i < SlotCount && count > 0; i++)
        {
            if (Ids[i] != id || Counts[i] >= StackLimit)
            {
                continue;
            }

            int room = StackLimit - Counts[i];
            int move = count < room ? count : room;
            Counts[i] += move;
            count -= move;
        }

        for (int i = 0; i < SlotCount && count > 0; i++)
        {
            if (Counts[i] != 0)
            {
                continue;
            }

            int move = count < StackLimit ? count : StackLimit;
            Ids[i] = id;
            Counts[i] = move;
            count -= move;
        }
    }
}
