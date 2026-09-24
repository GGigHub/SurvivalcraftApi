using System.Collections.Generic;

namespace SurvivalcraftGodot;

public static class BiomeChecks
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        const int seed = 42;

        BiomeColumn a = BiomeFill.Sample(seed, 12, -7);
        BiomeColumn b = BiomeFill.Sample(seed, 12, -7);
        if (a.SurfaceY != b.SurfaceY
            || a.SurfaceId != b.SurfaceId
            || a.HasOre != b.HasOre
            || a.OreY != b.OreY
            || a.OreId != b.OreId
            || !SameLayers(a.UndergroundIds, b.UndergroundIds))
        {
            failures.Add("同种子同坐标不稳定");
        }

        var surfaces = new HashSet<ushort>();
        bool foundOre = false;
        for (int z = -512; z <= 512; z += 8)
        {
            for (int x = -512; x <= 512; x += 8)
            {
                BiomeColumn col = BiomeFill.Sample(seed, x, z);
                surfaces.Add(col.SurfaceId);
                if (col.HasOre)
                {
                    foundOre = true;
                }
            }
        }

        if (surfaces.Count < 2)
        {
            failures.Add("至少两种不同地表");
        }

        if (!foundOre)
        {
            failures.Add("某个坐标能出矿");
        }

        return failures;
    }

    static bool SameLayers(ushort[] a, ushort[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
