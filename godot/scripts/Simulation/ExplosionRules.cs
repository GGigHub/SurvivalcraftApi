using System.Collections.Generic;

namespace SurvivalcraftGodot.Simulation;

/// <summary>
/// 爆炸破坏格子。对齐 SubsystemExplosions 的核心：压力向邻格传播，
/// 邻压之和 / 抗爆 &gt; 1 则挖成空气。火药桶压力取自 BlocksData。
/// </summary>
public static class ExplosionRules
{
    /// <summary>常见方块抗爆（BlocksData ExplosionResilience）。未列出的默认 20。</summary>
    public static float Resilience(int id) => id switch
    {
        ElectricIds.Air => 1f,
        ElectricIds.Dirt => 25f,
        ElectricIds.Granite => 75f,
        ElectricIds.SmallGunpowderKeg => 1f,
        ElectricIds.MediumGunpowderKeg => 1f,
        ElectricIds.LargeGunpowderKeg => 1f,
        _ => 20f
    };

    public static void Explode(SimGrid grid, GridPos center, float pressure)
    {
        if (pressure <= 0f)
        {
            return;
        }

        // 中心先炸掉自己
        grid.Set(center, ElectricIds.Air);

        var pressureMap = new Dictionary<GridPos, float> { [center] = pressure };
        var queue = new Queue<GridPos>();
        queue.Enqueue(center);
        var visited = new HashSet<GridPos> { center };

        while (queue.Count > 0)
        {
            GridPos p = queue.Dequeue();
            float here = pressureMap[p];
            if (here < 0.5f)
            {
                continue;
            }

            for (int face = 0; face < 6; face++)
            {
                GridPos n = p.Offset(face);
                if (!grid.InBounds(n) || visited.Contains(n))
                {
                    continue;
                }

                visited.Add(n);
                float neighborSum = SumNeighborPressure(pressureMap, n);
                // 传播：当前格压力衰减后写入邻格，再判定破坏
                float carry = here * 0.55f;
                pressureMap[n] = System.MathF.Max(pressureMap.GetValueOrDefault(n), carry);
                neighborSum = SumNeighborPressure(pressureMap, n);

                int id = grid.Get(n);
                if (id == ElectricIds.Air)
                {
                    queue.Enqueue(n);
                    continue;
                }

                float resilience = System.MathF.Max(Resilience(id), 1f);
                if (neighborSum / resilience > 1f)
                {
                    grid.Set(n, ElectricIds.Air);
                    queue.Enqueue(n);
                }
                // 实心挡压：不继续深入（对齐 IsCollidable 短路）
            }
        }
    }

    static float SumNeighborPressure(Dictionary<GridPos, float> map, GridPos p)
    {
        float sum = 0f;
        for (int face = 0; face < 6; face++)
        {
            GridPos n = p.Offset(face);
            if (map.TryGetValue(n, out float v))
            {
                sum += v;
            }
        }

        return sum;
    }

    /// <summary>统计中心周围 Chebyshev 半径内已被挖成空气的格数（不含中心）。</summary>
    public static int CountClearedAround(SimGrid grid, GridPos center, int radius, SimGrid? before = null)
    {
        int cleared = 0;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    int x = center.X + dx;
                    int y = center.Y + dy;
                    int z = center.Z + dz;
                    if (!grid.InBounds(x, y, z))
                    {
                        continue;
                    }

                    if (before != null && before.Get(x, y, z) == ElectricIds.Air)
                    {
                        continue;
                    }

                    if (grid.Get(x, y, z) == ElectricIds.Air)
                    {
                        cleared++;
                    }
                }
            }
        }

        return cleared;
    }
}
