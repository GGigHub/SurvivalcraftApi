namespace SurvivalcraftGodot;

public static class BlockIds
{
    public const ushort Air = 0;
    public const ushort Bedrock = 1;
    public const ushort Dirt = 2;
    public const ushort Granite = 3;
    public const ushort Stone = Granite;
    public const ushort Cobblestone = 5;
    public const ushort Glass = 15;
    public const ushort Sand = 7;
    public const ushort Grass = 8;
    public const ushort OakWood = 9;
    public const ushort Log = OakWood;
    public const ushort SpruceWood = 11;
    public const ushort OakLeaves = 12;
    public const ushort Leaves = OakLeaves;
    public const ushort Water = 18;
    public const ushort TallGrass = 19;
    public const ushort Plant = TallGrass;
    public const ushort Planks = 21;
    public const ushort Torch = 31;
    public const ushort Chest = 45;
    public const ushort WoodenStairs = 49;
    public const ushort Stairs = WoodenStairs;
    public const ushort WoodenDoor = 56;
    public const ushort Door = WoodenDoor;
    public const ushort Furnace = 64;
    public const ushort Dispenser = 216;
    public const ushort CopperPickaxe = 218;
    public const ushort WoodenTrapdoor = 83;
    public const ushort Trapdoor = WoodenTrapdoor;
    public const ushort RawMeat = 88;
    public const ushort Meat = RawMeat;
    public const ushort EmptyBucket = 90;
    public const ushort Bucket = EmptyBucket;
    public const ushort WaterBucket = 91;
    public const ushort BucketWater = WaterBucket;
    public const ushort Magma = 92;
    public const ushort MagmaBucket = 93;
    public const ushort BucketMagma = MagmaBucket;
    public const ushort WoodenFence = 94;
    public const ushort Fence = WoodenFence;
    public const ushort StoneSlab = 136;
    public const ushort Slab = StoneSlab;
    public const ushort Carpet = 208;

    /// <summary>区块尚未加载时的占位，碰撞和组网格都把它当成实心。</summary>
    public const ushort Boundary = ushort.MaxValue;

    public static bool IsSolid(ushort id) => id != Air;
}
