using Godot;
using System.Collections.Generic;
using System.IO;

namespace SurvivalcraftGodot;

public static class Acceptance
{
    public static bool Requested()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--acceptance")
            {
                return true;
            }
        }

        return false;
    }

    public static int Run()
    {
        var failures = new List<string>();
        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        Check(failures, "读到原版方块表", OriginalBlockData.Load(root) && OriginalBlockData.Count > 100);
        Check(failures, "读到原版楼梯模型", BlockModel.TryGet("Stairs", out ModelMesh stairs) && stairs.Positions.Length >= 12);
        Check(failures, "读到原版火把模型", BlockModel.TryGet("Torch", out ModelMesh torchMesh) && torchMesh.Positions.Length >= 12 && BlockModel.TryGet("Flame", out _) && TorchStandsUp(torchMesh) && BlockModel.TryGet("Slab", out ModelMesh slabMesh) && SlabSitsInCell(slabMesh));
        Check(failures, "读到原版木门模型", BlockModel.TryGet("Door", out ModelMesh doorMesh) && doorMesh.Positions.Length >= 12);
        Check(failures, "读到原版熔炉模型", BlockModel.TryGet("Furnace", out ModelMesh furnaceMesh) && furnaceMesh.Positions.Length >= 12);
        Check(failures, "读到原版按钮模型", BlockModel.TryGet("Button", out ModelMesh buttonMesh) && buttonMesh.Positions.Length >= 12);
        Check(failures, "泥土编号与原版一致", OriginalBlockData.IndexOf("DirtBlock") == BlockIds.Dirt);
        Check(failures, "草地方块编号与原版一致", OriginalBlockData.IndexOf("GrassBlock") == BlockIds.Grass);
        Check(failures, "水编号与原版一致", OriginalBlockData.IndexOf("WaterBlock") == BlockIds.Water);
        Check(failures, "原版草掉落泥土", BlockCatalog.DropOf(BlockIds.Grass) == BlockIds.Dirt);
        Check(failures, "草皮顶面贴图槽", BlockCatalog.FaceSlot(BlockIds.Grass, 2) == 0);
        Check(failures, "草皮底面贴图槽", BlockCatalog.FaceSlot(BlockIds.Grass, 3) == 2);
        Check(failures, "泥土贴图槽", OriginalBlockData.TryGet(BlockIds.Dirt, out OriginalBlockRow dirtRow) && dirtRow.TextureSlot == 2);
        Check(failures, "方块图集文件", File.Exists(Path.Combine(root, "godot", "assets", "textures", "Blocks.webp")));
        Check(failures, "生成地表是草", TerrainGenerator.SurfaceY(0, 0) > 0);
        var world = new VoxelWorld();
        world.Generate(new ChunkPos(0, 0));
        int surface = TerrainGenerator.SurfaceY(0, 0);
        ushort expectedSurface = BiomeFill.Sample(TerrainGenerator.Seed, 0, 0).SurfaceId;
        Check(failures, "地表方块", world.GetBlock(0, surface, 0) == expectedSurface);
        bool sawPlant = false;
        for (int z = 0; z < Chunk.Size && !sawPlant; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                int y = TerrainGenerator.SurfaceY(x, z) + 1;
                if (world.GetBlock(x, y, z) == BlockIds.Plant)
                {
                    sawPlant = true;
                    break;
                }
            }
        }

        Check(failures, "草地上长出草丛", sawPlant);
        Check(failures, "挖掉后变空气", world.SetBlock(0, surface, 0, BlockIds.Air) && world.GetBlock(0, surface, 0) == BlockIds.Air);
        for (int y = surface + 8; y >= surface - 1; y--)
        {
            world.SetBlock(2, y, 2, BlockIds.Air);
        }

        world.SetBlock(3, surface - 1, 2, BlockIds.Air);
        Check(failures, "挖开头顶后阳光落下", world.SampleSky(2, surface - 1, 2) == 15);
        Check(failures, "阳光横向照进旁边", world.SampleSky(3, surface - 1, 2) >= 13);
        world.SetBlock(4, surface + 2, 4, BlockIds.Torch);
        Check(failures, "火把照亮旁边", world.GetBlockLight(5, surface + 2, 4) >= 13);
        world.SetBlock(4, surface + 2, 4, BlockIds.Air);
        Check(failures, "拿走火把后变暗", world.GetBlockLight(5, surface + 2, 4) == 0);

        var inventory = new Inventory();
        inventory.Add(BlockCatalog.DropOf(BlockIds.Grass), 1);
        Check(failures, "草掉落泥土", Has(inventory, BlockIds.Dirt));
        Check(failures, "没有木头不能合成", Crafting.TryMakePlanks(inventory) == false);
        inventory.Add(BlockIds.Log, 1);
        Check(failures, "合成成功", Crafting.TryMakePlanks(inventory));
        Check(failures, "得到木板", Count(inventory, BlockIds.Planks) == 4);
        foreach (string craftFailure in CraftChecks.Run(root))
        {
            failures.Add(craftFailure);
        }

        RecipeBook smeltBook = RecipeBook.Load(
            Path.Combine(root, "Survivalcraft", "Content", "Assets", "CraftingRecipes.xml"),
            Path.Combine(root, "Survivalcraft", "Block"),
            Path.Combine(root, "Survivalcraft", "Content", "Assets", "BlocksData.txt"));
        var furnace = new Furnace();
        int sandId = OriginalBlockData.IndexOf("SandBlock");
        int glassId = OriginalBlockData.IndexOf("GlassBlock");
        Check(failures, "木板能当燃料", furnace.AddFuel(BlockIds.Planks, 1));
        Check(failures, "沙子放进熔炉", furnace.AddInput((ushort)sandId, 1));
        furnace.Tick(10f, smeltBook);
        Check(failures, "沙子烧成玻璃", furnace.OutputId == glassId && furnace.OutputCount >= 1 && furnace.InputCount == 0);
        var dispensers = new DispenserStore();
        var dispenserInventory = new Inventory();
        dispenserInventory.SetSlot(0, BlockIds.Sand, 1);
        world.SetBlock(2, 80, 2, BlockIds.Dispenser, 0);
        world.SetBlock(3, 80, 2, BlockIds.Air);
        var dispenserHit = new VoxelHit { Hit = true, Block = new Vector3I(2, 80, 2), Previous = new Vector3I(2, 80, 3) };
        Check(failures, "沙子装进发射器", dispensers.TryInsert(dispenserInventory, 0, dispenserHit, out _));
        Check(failures, "按钮让发射器弹出沙子", dispensers.TryEject(world, 2, 80, 2, out _) && world.GetBlock(3, 80, 2) == BlockIds.Sand);
        Check(failures, "铜镐有耐久", OriginalBlockData.TryGet(BlockIds.CopperPickaxe, out OriginalBlockRow pick) && pick.Durability > 1);
        var worn = new Inventory();
        worn.SetSlot(0, BlockIds.CopperPickaxe, 1);
        int maxWear = OriginalBlockData.TryGet(BlockIds.CopperPickaxe, out OriginalBlockRow pickRow) ? pickRow.Durability : 0;
        for (int i = 0; i < maxWear - 1; i++)
        {
            worn.ApplyWear(0, out _);
        }

        Check(failures, "镐还没坏", worn.Ids[0] == BlockIds.CopperPickaxe && worn.Wear[0] == maxWear - 1);
        Check(failures, "镐挖到上限会坏", worn.ApplyWear(0, out string brokenTool) && brokenTool.Length > 0 && worn.Counts[0] == 0);

        foreach (string biomeFailure in BiomeChecks.Run())
        {
            failures.Add(biomeFailure);
        }

        foreach (string electricFailure in ElectricChecks.Run())
        {
            failures.Add(electricFailure);
        }

        world.SetBlock(2, 30, 2, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Button);
        world.SetBlock(3, 30, 2, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Wire);
        world.SetBlock(4, 30, 2, (ushort)SurvivalcraftGodot.Simulation.ElectricIds.LightBulb);
        var live = new WorldElectric();
        live.Rebuild(world, 3, 30, 2);
        Check(failures, "能按世界里的按钮", live.PressButton(2, 30, 2));
        for (int i = 0; i < 8; i++)
        {
            live.Step(world);
        }

        Check(failures, "世界里的灯被点亮", world.GetState(4, 30, 2) == 1);

        world.SetBlock(2, 20, 2, BlockIds.Stone);
        world.SetBlock(3, 21, 2, BlockIds.Air);
        world.SetBlock(3, 20, 2, BlockIds.Stone);
        world.SetBlock(2, 21, 2, BlockIds.Water, 0);
        world.TickFlow(16);
        Check(failures, "水向旁边流", world.GetBlock(3, 21, 2) == BlockIds.Water);

        byte[] saved = WorldSnapshot.Write(world, inventory, 12.5f);
        var loadedWorld = new VoxelWorld();
        var loadedInventory = new Inventory();
        bool loaded = WorldSnapshot.TryRead(saved, loadedWorld, loadedInventory, out float time, out string error);
        Check(failures, "存档读回", loaded && error.Length == 0 && time == 12.5f);
        Check(failures, "存档方块还在", loadedWorld.GetBlock(2, 20, 2) == BlockIds.Stone);
        Check(failures, "拒绝旧档", !WorldSnapshot.TryRead("<Project>"u8.ToArray(), new VoxelWorld(), new Inventory(), out _, out string reject) && reject.Length > 0);

        Vector3 above = new(0.5f, 20.6f, 0.5f);
        Vector3 inside = new(0.5f, 20.1f, 0.5f);
        world.SetBlock(0, 20, 0, BlockIds.Slab);
        Check(failures, "半砖上方不挡", !VoxelMotion.OverlapsCell(BlockIds.Slab, 0, 0, 20, 0, above));
        Check(failures, "半砖下半挡人", VoxelMotion.OverlapsCell(BlockIds.Slab, 0, 0, 20, 0, inside));
        Vector3 inCell = new(0.5f, 20.1f, 0.5f);
        Check(failures, "树叶挡住人", VoxelMotion.OverlapsCell(BlockIds.Leaves, 0, 0, 20, 0, inCell));
        Check(failures, "草丛不挡人", !VoxelMotion.OverlapsCell(BlockIds.Plant, 0, 0, 20, 0, inCell));
        int open = surface + 15;
        world.SetBlock(6, open, 4, BlockIds.Stone);
        world.SetBlock(6, open, 5, BlockIds.Plant);
        VoxelHit throughGrass = VoxelMotion.Raycast(world, new Vector3(6.5f, open + 0.5f, 7.5f), new Vector3(0f, 0f, -1f), 8f, placing: true);
        Check(failures, "放置穿过草丛打到石头", throughGrass.Hit && throughGrass.Block == new Vector3I(6, open, 4));
        world.SetBlock(6, open, 5, BlockIds.Leaves);
        VoxelHit onLeaves = VoxelMotion.Raycast(world, new Vector3(6.5f, open + 0.5f, 7.5f), new Vector3(0f, 0f, -1f), 8f, placing: true);
        Check(failures, "放置停在树叶上", onLeaves.Hit && onLeaves.Block == new Vector3I(6, open, 5));
        world.SetBlock(8, open, 8, BlockIds.Door, 0);
        Vector3 inDoor = new(8.5f, open + 0.2f, 8.5f);
        Check(failures, "关着的门挡住人", VoxelMotion.OverlapsCell(BlockIds.Door, 0, 8, open, 8, inDoor));
        byte opened = BlockShapes.ToggleDoor(0);
        Check(failures, "右键把门打开", BlockShapes.DoorOpen(opened));
        Check(failures, "打开的门让出路", !VoxelMotion.OverlapsCell(BlockIds.Door, opened, 8, open, 8, inDoor));

        var vitals = new Vitals();
        vitals.Tick(10f, true, false, 1f);
        Check(failures, "饥饿会下降", vitals.Hunger < 20f);
        Check(failures, "水里会湿", vitals.Wetness > 0f);
        float beforeEat = vitals.Hunger;
        vitals.Eat();
        Check(failures, "吃东西回饥饿", vitals.Hunger > beforeEat);
        float beforeMagma = vitals.Health;
        vitals.Tick(1f, false, true, 1f);
        Check(failures, "岩浆掉血", vitals.Health < beforeMagma);
        var drowning = new Vitals();
        float beforeDrown = drowning.Health;
        drowning.Tick(10f, true, false, 0.2f, true);
        Check(failures, "头埋在水里会溺水", drowning.Air < 15f && drowning.Health < beforeDrown);
        float springRain = WorldClimate.DayLength * 0.5f;
        float summerStorm = WorldClimate.DayLength * 4.5f;
        float winterSnow = WorldClimate.DayLength * 12.5f;
        float clearMorning = WorldClimate.DayLength * 0.1f;
        Check(failures, "春天午后下雨", WorldClimate.WeatherAt(springRain) == WeatherKind.Rain);
        Check(failures, "夏天打雷", WorldClimate.WeatherAt(summerStorm) == WeatherKind.Thunder);
        Check(failures, "冬天下雪", WorldClimate.WeatherAt(winterSnow) == WeatherKind.Snow && WorldClimate.SeasonIndex(winterSnow) == 3);
        Check(failures, "清晨是晴天", WorldClimate.WeatherAt(clearMorning) == WeatherKind.Clear);
        var wet = new Vitals();
        wet.Tick(4f, false, false, 1f, false, true, 0);
        Check(failures, "淋雨会变湿", wet.Wetness > 0f);
        var ranked = new Vitals();
        ranked.AddExperience(1);
        Check(failures, "挖方块会得到经验", ranked.Level > 1f && ranked.LevelRank == 1);
        float bolt = 0f;
        Check(failures, "雷暴不会马上劈下来", !WorldClimate.AdvanceBolt(ref bolt, 10f));
        Check(failures, "雷暴持续后会落雷", WorldClimate.AdvanceBolt(ref bolt, 31f));
        byte[] leveled = WorldSnapshot.Write(world, inventory, 1f, 0f, 0f, 0f, 20f, 20f, 2.5f);
        Check(failures, "存档记下等级", WorldSnapshot.TryRead(leveled, new VoxelWorld(), new Inventory(), out _, out _) && WorldSnapshot.SavedLevel == 2.5f);

        byte[] posed = WorldSnapshot.Write(world, inventory, 4f, 3f, 9f, 5f, 11f, 8f);
        var poseWorld = new VoxelWorld();
        var poseInventory = new Inventory();
        Check(failures, "存档记下人的位置", WorldSnapshot.TryRead(posed, poseWorld, poseInventory, out _, out _) && WorldSnapshot.HasPlayerPose && WorldSnapshot.SavedX == 3f && WorldSnapshot.SavedHealth == 11f);

        var chests = new ChestStore();
        chests.Deposit(1, 2, 3, BlockIds.Dirt, 3);
        Check(failures, "箱子存入", chests.CountOf(1, 2, 3, BlockIds.Dirt) == 3);
        Check(failures, "箱子取出", chests.TakeOne(1, 2, 3) == BlockIds.Dirt);

        inventory.SetSlot(0, BlockIds.Bucket, 1);
        world.SetBlock(4, 21, 2, BlockIds.Water);
        var bucketHit = new VoxelHit { Hit = true, Block = new Vector3I(4, 21, 2), Previous = new Vector3I(4, 22, 2) };
        Check(failures, "桶装水", BucketActions.TryUse(inventory, 0, world, bucketHit) && inventory.Ids[0] == BlockIds.BucketWater);
        world.SetBlock(5, 21, 2, BlockIds.Air);
        world.SetBlock(5, 20, 2, BlockIds.Stone);
        var pourHit = new VoxelHit { Hit = true, Block = new Vector3I(5, 20, 2), Previous = new Vector3I(5, 21, 2) };
        Check(failures, "桶倒水", BucketActions.TryUse(inventory, 0, world, pourHit) && world.GetBlock(5, 21, 2) == BlockIds.Water);

        world.SetBlock(8, 20, 6, BlockIds.Stone);
        world.SetBlock(9, 20, 6, BlockIds.Stone);
        world.SetBlock(9, 21, 6, BlockIds.Air);
        Vector3 stepped = CreatureMind.StepToward(world, new Vector3(8.5f, 21f, 6.5f), new Vector3(12f, 21f, 6.5f), 1f);
        Check(failures, "生物沿格子走", stepped.X > 8.5f);
        Vector3 pushed = CreatureMind.PushOut(new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 1f), 1f);
        Check(failures, "人和生物会推开", pushed.X > 1f);
        Vector3 aside = CreatureMind.PushOut(new Vector3(1.2f, 0f, 1f), new Vector3(1f, 0f, 1f), 1f);
        Check(failures, "站得太近会被推开", aside.X > 1.2f);

        if (failures.Count == 0)
        {
            GD.Print("ACCEPTANCE OK");
            return 0;
        }

        foreach (string failure in failures)
        {
            GD.PrintErr("ACCEPTANCE FAIL: " + failure);
        }

        return 1;
    }

    static bool TorchStandsUp(ModelMesh mesh)
    {
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (Vector3 point in mesh.Positions)
        {
            minY = Mathf.Min(minY, point.Y);
            maxY = Mathf.Max(maxY, point.Y);
        }

        return minY >= -0.02f && maxY > 0.4f && maxY < 0.8f;
    }

    static bool SlabSitsInCell(ModelMesh mesh)
    {
        float maxY = float.MinValue;
        float minY = float.MaxValue;
        foreach (Vector3 point in mesh.Positions)
        {
            minY = Mathf.Min(minY, point.Y);
            maxY = Mathf.Max(maxY, point.Y);
            if (point.X < -0.6f || point.X > 0.6f || point.Z < -0.6f || point.Z > 0.6f)
            {
                return false;
            }
        }

        return minY >= -0.05f && maxY > 0.2f && maxY < 0.7f;
    }

    static void Check(List<string> failures, string name, bool ok)
    {
        if (!ok)
        {
            failures.Add(name);
        }
    }

    static bool Has(Inventory inventory, ushort id)
    {
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            if (inventory.Ids[i] == id && inventory.Counts[i] > 0)
            {
                return true;
            }
        }

        return false;
    }

    static int Count(Inventory inventory, ushort id)
    {
        int total = 0;
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            if (inventory.Ids[i] == id)
            {
                total += inventory.Counts[i];
            }
        }

        return total;
    }
}
