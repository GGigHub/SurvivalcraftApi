namespace SurvivalcraftGodot;

public static class BucketActions
{
    public static bool TryUse(Inventory inventory, int slot, VoxelWorld world, VoxelHit hit)
    {
        if (slot < 0 || slot >= Inventory.SlotCount || !hit.Hit)
        {
            return false;
        }

        ushort held = inventory.Ids[slot];
        ushort looking = world.GetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z);
        if (held == BlockIds.Bucket && (looking == BlockIds.Water || looking == BlockIds.Magma))
        {
            world.SetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z, BlockIds.Air);
            inventory.SetSlot(slot, looking == BlockIds.Water ? BlockIds.BucketWater : BlockIds.BucketMagma, 1);
            return true;
        }

        if ((held == BlockIds.BucketWater || held == BlockIds.BucketMagma)
            && world.GetBlock(hit.Previous.X, hit.Previous.Y, hit.Previous.Z) == BlockIds.Air)
        {
            ushort fluid = held == BlockIds.BucketWater ? BlockIds.Water : BlockIds.Magma;
            if (!world.SetBlock(hit.Previous.X, hit.Previous.Y, hit.Previous.Z, fluid, 0))
            {
                return false;
            }

            inventory.SetSlot(slot, BlockIds.Bucket, 1);
            return true;
        }

        return false;
    }
}
