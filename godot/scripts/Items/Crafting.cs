namespace SurvivalcraftGodot;

public static class Crafting
{
    public static bool TryMakePlanks(Inventory inventory)
    {
        int logSlot = -1;
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            if (inventory.Ids[i] == BlockIds.Log && inventory.Counts[i] > 0)
            {
                logSlot = i;
                break;
            }
        }

        if (logSlot < 0 || !inventory.TryConsume(logSlot))
        {
            return false;
        }

        inventory.Add(BlockIds.Planks, 4);
        return true;
    }
}
