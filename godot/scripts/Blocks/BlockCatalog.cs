namespace SurvivalcraftGodot;

public enum BlockShape
{
    Empty,
    Solid,
    Slab,
    Stairs,
    Cross,
    Torch,
    Door,
    Trapdoor,
    Fence,
    Carpet,
    Liquid,
}

public static class BlockCatalog
{
    public static string NameOf(ushort id) => id switch
    {
        BlockIds.Planks => "木板",
        BlockIds.Slab => "半砖",
        BlockIds.Plant => "草丛",
        BlockIds.Door => "门",
        BlockIds.Furnace => "熔炉",
        BlockIds.Dispenser => "发射器",
        BlockIds.CopperPickaxe => "铜镐",
        (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Button => "按钮",
        BlockIds.Sand => "沙子",
        BlockIds.Glass => "玻璃",
        BlockIds.Stairs => "楼梯",
        BlockIds.Fence => "栅栏",
        BlockIds.Torch => "火把",
        BlockIds.Trapdoor => "活板门",
        BlockIds.Carpet => "地毯",
        BlockIds.Water => "水",
        BlockIds.Magma => "岩浆",
        BlockIds.Chest => "箱子",
        BlockIds.Bucket => "空桶",
        BlockIds.BucketWater => "水桶",
        BlockIds.BucketMagma => "岩浆桶",
        BlockIds.Meat => "肉",
        BlockIds.Log => "木头",
        BlockIds.Leaves => "树叶",
        BlockIds.Grass => "草地方块",
        BlockIds.Dirt => "泥土",
        BlockIds.Stone => "石头",
        _ => OriginalBlockData.TryGet(id, out OriginalBlockRow named) ? named.ClassName : "方块",
    };

    public static BlockShape ShapeOf(ushort id) => id switch
    {
        BlockIds.Air => BlockShape.Empty,
        BlockIds.Slab => BlockShape.Slab,
        BlockIds.Stairs => BlockShape.Stairs,
        BlockIds.Plant => BlockShape.Cross,
        BlockIds.Torch => BlockShape.Torch,
        BlockIds.Door => BlockShape.Door,
        BlockIds.Trapdoor => BlockShape.Trapdoor,
        BlockIds.Fence => BlockShape.Fence,
        BlockIds.Carpet => BlockShape.Carpet,
        BlockIds.Water => BlockShape.Liquid,
        BlockIds.Magma => BlockShape.Liquid,
        BlockIds.Bucket => BlockShape.Empty,
        BlockIds.BucketWater => BlockShape.Empty,
        BlockIds.BucketMagma => BlockShape.Empty,
        BlockIds.Meat => BlockShape.Empty,
        BlockIds.Boundary => BlockShape.Solid,
        _ => BlockShape.Solid,
    };

    public static bool BlocksSun(ushort id)
    {
        BlockShape shape = ShapeOf(id);
        return shape != BlockShape.Empty
            && shape != BlockShape.Cross
            && shape != BlockShape.Torch
            && shape != BlockShape.Carpet
            && shape != BlockShape.Liquid;
    }

    public static bool FullCell(ushort id) => ShapeOf(id) == BlockShape.Solid;

    public static int FaceSlot(ushort id, int meshFace)
    {
        int originalFace = meshFace switch
        {
            0 => 1,
            1 => 3,
            2 => 4,
            3 => 5,
            4 => 0,
            _ => 2,
        };
        if (id == BlockIds.Grass)
        {
            return originalFace switch
            {
                4 => 0,
                5 => 2,
                _ => 3,
            };
        }

        if (OriginalBlockData.TryGet(id, out OriginalBlockRow row))
        {
            return row.TextureSlot;
        }

        return 0;
    }

    public static void SlotUv(int slot, out float u0, out float v0, out float u1, out float v1)
    {
        int column = slot % 16;
        int row = slot / 16;
        u0 = column / 16f;
        v0 = row / 16f;
        u1 = (column + 1) / 16f;
        v1 = (row + 1) / 16f;
    }
    public static ushort DropOf(ushort id)
    {
        if (OriginalBlockData.TryGet(id, out OriginalBlockRow row))
        {
            return row.DropId;
        }

        return id == BlockIds.Air ? BlockIds.Air : id;
    }

    public static byte EmittedLight(ushort id, byte state)
    {
        if (id == BlockIds.Torch)
        {
            return 15;
        }

        if (id == BlockIds.Magma)
        {
            return 13;
        }

        if (id == (ushort)SurvivalcraftGodot.Simulation.ElectricIds.LightBulb)
        {
            return state == 0 ? (byte)0 : (byte)15;
        }

        return 0;
    }

    public static bool PassesLight(ushort id) => !BlocksSun(id) || EmittedLight(id, 1) > 0;

    public static int LightAttenuation(ushort id) => ShapeOf(id) == BlockShape.Liquid ? 1 : 0;

    public static bool Collides(ushort id)
    {
        if (OriginalBlockData.TryGet(id, out OriginalBlockRow row))
        {
            return row.Collidable;
        }

        BlockShape shape = ShapeOf(id);
        return shape != BlockShape.Empty
            && shape != BlockShape.Cross
            && shape != BlockShape.Torch
            && shape != BlockShape.Carpet
            && shape != BlockShape.Liquid;
    }

    public static bool PlacementTransparent(ushort id)
    {
        if (OriginalBlockData.TryGet(id, out OriginalBlockRow row))
        {
            return row.PlacementTransparent;
        }

        BlockShape shape = ShapeOf(id);
        return shape == BlockShape.Cross || shape == BlockShape.Torch || shape == BlockShape.Empty;
    }

    public static bool StopsRay(ushort id, bool placing)
    {
        if (id == BlockIds.Air)
        {
            return false;
        }

        return placing ? !PlacementTransparent(id) : !DiggingTransparent(id);
    }

    public static bool DiggingTransparent(ushort id)
    {
        if (OriginalBlockData.TryGet(id, out OriginalBlockRow row))
        {
            return row.DiggingTransparent;
        }

        return id == BlockIds.Air;
    }
}
