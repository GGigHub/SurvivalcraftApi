using System;

namespace SurvivalcraftGodot;

/// <summary>
/// 按种子与水平坐标采样一列地表/地下/矿脉。规则参考 TerrainContentsGenerator24 的温度湿度与地表替换，不依赖旧引擎类型。
/// </summary>
public readonly struct BiomeColumn
{
    public int SurfaceY { get; init; }
    public ushort SurfaceId { get; init; }

    /// <summary>地表以下若干层，[0] 为 SurfaceY-1。</summary>
    public ushort[] UndergroundIds { get; init; }

    public bool HasOre { get; init; }
    public int OreY { get; init; }
    public ushort OreId { get; init; }
}

public static class BiomeFill
{
    // 原版 Index，未写入 BlockIds.cs 的常量放在本文件。
    public const ushort Sandstone = 4;
    public const ushort Gravel = 6;
    public const ushort Sand = 7;
    public const ushort CoalOre = 16;
    public const ushort IronOre = 39;
    public const ushort CopperOre = 41;
    public const ushort Snow = 61;
    public const ushort Ice = 62;

    public const int UndergroundDepth = 8;

    public static BiomeColumn Sample(int seed, int x, int z)
    {
        int temperature = Temperature(seed, x, z);
        int humidity = Humidity(seed, x, z);
        float mountain = MountainFactor(seed, x, z);
        int surfaceY = Height(seed, x, z);
        bool beach = IsBeach(temperature, humidity, mountain, seed, x, z);
        bool frozen = IsFrozen(temperature, surfaceY);

        ushort surfaceId = ChooseSurface(temperature, humidity, mountain, surfaceY, beach, frozen);
        ushort[] under = BuildUnderground(surfaceId, beach);

        TryPlaceOre(seed, x, z, surfaceY, out bool hasOre, out int oreY, out ushort oreId);
        if (hasOre)
        {
            int depthIndex = surfaceY - 1 - oreY;
            if (depthIndex >= 0 && depthIndex < under.Length)
            {
                under[depthIndex] = oreId;
            }
        }

        return new BiomeColumn
        {
            SurfaceY = surfaceY,
            SurfaceId = surfaceId,
            UndergroundIds = under,
            HasOre = hasOre,
            OreY = oreY,
            OreId = oreId,
        };
    }

    public static int Height(int seed, int x, int z)
    {
        float mountain = MountainFactor(seed, x, z);
        float wave = MathF.Sin((x + seed) * 0.07f) * 5f
            + MathF.Cos((z - seed) * 0.05f) * 4f
            + MathF.Sin((x * 0.31f + z * 0.27f + seed) * 0.04f) * 8f
            + mountain * 40f
            + ValueNoise(x * 0.02f + seed, z * 0.02f - seed) * 6f;
        return Math.Clamp(48 + (int)wave, 12, 200);
    }

    public static int Temperature(int seed, int x, int z)
    {
        // 对应 CalculateTemperature：低频主趋势 + 中频细节，保证同种子稳定且群系有宽度。
        float n = 0.65f * ValueNoise(x * 0.012f + seed * 0.17f, z * 0.012f + seed * 0.11f)
            + 0.35f * ValueNoise(x * 0.04f + 19.3f, z * 0.04f + 7.1f);
        return Math.Clamp((int)(n * 16f), 0, 15);
    }

    public static int Humidity(int seed, int x, int z)
    {
        float n = 0.65f * ValueNoise(x * 0.01f + seed * 0.23f + 40f, z * 0.01f + seed * 0.19f + 90f)
            + 0.35f * ValueNoise(x * 0.035f + 55.5f, z * 0.035f + 12.2f);
        return Math.Clamp((int)(n * 16f), 0, 15);
    }

    public static float MountainFactor(int seed, int x, int z)
    {
        // 抬高尾部，便于出现裸岩山顶（原版 mountain 接近 1 时地表层变薄）。
        float n = ValueNoise(x * 0.015f + seed * 0.31f, z * 0.015f - seed * 0.13f);
        float detail = ValueNoise(x * 0.05f + 2.4f, z * 0.05f + 8.8f);
        return Math.Clamp(MathF.Pow(0.55f * n + 0.45f * detail, 0.7f), 0f, 1f);
    }

    static ushort ChooseSurface(int temperature, int humidity, float mountain, int surfaceY, bool beach, bool frozen)
    {
        // GenerateSurface：砂岩带 → 沙/砂砾；极高且结冰 → 冰；山地表层变薄 → 裸花岗岩；其余泥土后长草，结冰处覆雪。
        if (beach)
        {
            return temperature > 4 && temperature < 7 ? Gravel : Sand;
        }

        if (surfaceY > 120 && frozen)
        {
            return Ice;
        }

        float thinSurface = Math.Clamp((mountain - 0.88f) / 0.12f, 0f, 1f)
            - Math.Clamp((humidity - 3f) / 12f, 0f, 1f);
        if (thinSurface > 0.45f)
        {
            return BlockIds.Granite;
        }

        if (frozen)
        {
            return Snow;
        }

        return BlockIds.Grass;
    }

    static ushort[] BuildUnderground(ushort surfaceId, bool beach)
    {
        var layers = new ushort[UndergroundDepth];
        for (int i = 0; i < UndergroundDepth; i++)
        {
            if (beach || surfaceId == Sand || surfaceId == Gravel)
            {
                layers[i] = i < 3 ? (surfaceId == Gravel ? Gravel : Sand) : (i < 5 ? Sandstone : BlockIds.Granite);
            }
            else if (surfaceId == Ice || surfaceId == Snow)
            {
                layers[i] = i < 2 ? (surfaceId == Ice ? Ice : BlockIds.Dirt) : (i < 4 ? BlockIds.Dirt : BlockIds.Granite);
            }
            else if (surfaceId == BlockIds.Granite)
            {
                layers[i] = BlockIds.Granite;
            }
            else
            {
                layers[i] = i < 4 ? BlockIds.Dirt : BlockIds.Granite;
            }
        }

        return layers;
    }

    static void TryPlaceOre(int seed, int x, int z, int surfaceY, out bool hasOre, out int oreY, out ushort oreId)
    {
        hasOre = false;
        oreY = 0;
        oreId = 0;

        // GenerateMinerals：煤偏广、铜中层、铁偏深；按列确定性抽样。
        int coalRoll = Hash(seed, x, z, 1009);
        if (coalRoll % 11 == 0)
        {
            int y = 5 + (coalRoll % 40);
            if (y < surfaceY - 1)
            {
                hasOre = true;
                oreY = y;
                oreId = CoalOre;
                return;
            }
        }

        int copperRoll = Hash(seed, x + 17, z - 9, 2003);
        if (copperRoll % 13 == 0)
        {
            int y = 20 + (copperRoll % 40);
            if (y < surfaceY - 1)
            {
                hasOre = true;
                oreY = y;
                oreId = CopperOre;
                return;
            }
        }

        int ironRoll = Hash(seed, x - 5, z + 21, 3001);
        if (ironRoll % 15 == 0)
        {
            int y = 2 + (ironRoll % 35);
            if (y < surfaceY - 1)
            {
                hasOre = true;
                oreY = y;
                oreId = IronOre;
            }
        }
    }

    static bool IsBeach(int temperature, int humidity, float mountain, int seed, int x, int z)
    {
        // 原版：炎热干燥或近岸且非高山 → 砂岩带，地表再换成沙/砂砾。
        if (mountain >= 0.97f)
        {
            return false;
        }

        if (temperature > 8 && humidity < 8)
        {
            return true;
        }

        return MathF.Abs(OceanShoreDistance(seed, x, z)) < 16f;
    }

    static float OceanShoreDistance(int seed, int x, int z)
    {
        // 简化海岸线：一条随种子平移、带起伏的岸。
        float corner = (seed % 200) - 100f;
        return (x - corner) + 10f * MathF.Sin(z * 0.05f + seed * 0.01f);
    }

    static bool IsFrozen(int temperature, int y)
    {
        // SubsystemWeather.IsPlaceFrozen / GetTemperatureAdjustmentAtHeight
        int adjust = y > 64
            ? (int)MathF.Round(-0.0008f * (y - 64) * (y - 64))
            : (int)MathF.Round(0.1f * (64 - y));
        return temperature + adjust <= 0;
    }

    static float ValueNoise(float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float fx = x - x0;
        float fz = z - z0;
        float a = Hash01(x0, z0);
        float b = Hash01(x0 + 1, z0);
        float c = Hash01(x0, z0 + 1);
        float d = Hash01(x0 + 1, z0 + 1);
        float u = fx * fx * (3f - 2f * fx);
        float v = fz * fz * (3f - 2f * fz);
        return Math.Clamp(Lerp(Lerp(a, b, u), Lerp(c, d, u), v), 0f, 1f);
    }

    static float Hash01(int x, int z)
    {
        unchecked
        {
            int n = (x * 374761393) + (z * 668265263);
            n = (n ^ (n >> 13)) * 1274126177;
            return (n & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    static int Hash(int seed, int x, int z, int salt)
    {
        unchecked
        {
            int n = seed * 73856093 ^ x * 19349663 ^ z * 83492791 ^ salt * 39916801;
            n = (n ^ (n >> 13)) * 1274126177;
            return n & 0x7fffffff;
        }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
