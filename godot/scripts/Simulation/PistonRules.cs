namespace SurvivalcraftGodot.Simulation;

/// <summary>
/// 活塞瞬时推动（无动画）。对齐 MovePiston 推出时的格子结果：
/// 前方可动方块整体前移，活塞头占据伸出空间，终点必须是空气。
/// </summary>
public static class PistonRules
{
    public const int MaxMovedBlocks = 8;

    public static bool Adjust(SimGrid grid, GridPos pistonPos, int face, int length)
    {
        if (grid.Get(pistonPos) != ElectricIds.Piston)
        {
            return false;
        }

        length = System.Math.Clamp(length, 0, 8);
        int current = CountHeads(grid, pistonPos, face);
        if (length == current)
        {
            return true;
        }

        if (length > current)
        {
            for (int i = 0; i < length - current; i++)
            {
                if (!ExtendOne(grid, pistonPos, face))
                {
                    return false;
                }
            }

            return true;
        }

        for (int i = 0; i < current - length; i++)
        {
            RetractOne(grid, pistonPos, face);
        }

        return true;
    }

    public static bool PushOne(SimGrid grid, GridPos pistonPos, int face) =>
        Adjust(grid, pistonPos, face, CountHeads(grid, pistonPos, face) + 1);

    public static int CountHeads(SimGrid grid, GridPos piston, int face)
    {
        int n = 0;
        GridPos p = piston.Offset(face);
        while (grid.InBounds(p) && grid.Get(p) == ElectricIds.PistonHead)
        {
            n++;
            p = p.Offset(face);
        }

        return n;
    }

    static bool ExtendOne(SimGrid grid, GridPos piston, int face)
    {
        int heads = CountHeads(grid, piston, face);
        // 活塞头链之后的第一格
        GridPos first = piston;
        for (int i = 0; i <= heads; i++)
        {
            first = first.Offset(face);
        }

        // 收集可推方块（不含即将写入的新活塞头格；若该格已有可动方块则一并推）
        var stack = new System.Collections.Generic.List<(GridPos Pos, int Id)>();
        GridPos scan = first;
        while (stack.Count < MaxMovedBlocks)
        {
            if (!grid.InBounds(scan))
            {
                return false;
            }

            int id = grid.Get(scan);
            if (id == ElectricIds.Air)
            {
                break;
            }

            if (!IsMovable(id))
            {
                return false;
            }

            stack.Add((scan, id));
            scan = scan.Offset(face);
        }

        // 终点：最后一块前面必须是空气（或没有方块可推且 first 是空气）
        GridPos landing = stack.Count == 0 ? first : stack[^1].Pos.Offset(face);
        if (!grid.InBounds(landing) || grid.Get(landing) != ElectricIds.Air)
        {
            return false;
        }

        // 远端先行
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            GridPos to = stack[i].Pos.Offset(face);
            grid.Set(to, stack[i].Id);
            grid.Set(stack[i].Pos, ElectricIds.Air);
        }

        // 旧活塞头整体前移一格
        for (int i = heads; i >= 1; i--)
        {
            GridPos from = piston;
            for (int k = 0; k < i; k++)
            {
                from = from.Offset(face);
            }

            grid.Set(from.Offset(face), ElectricIds.PistonHead);
            grid.Set(from, ElectricIds.Air);
        }

        // 活塞正前方确保有头
        grid.Set(piston.Offset(face), ElectricIds.PistonHead);
        return true;
    }

    static void RetractOne(SimGrid grid, GridPos piston, int face)
    {
        int heads = CountHeads(grid, piston, face);
        if (heads <= 0)
        {
            return;
        }

        GridPos tip = piston;
        for (int i = 0; i < heads; i++)
        {
            tip = tip.Offset(face);
        }

        grid.Set(tip, ElectricIds.Air);
    }

    public static bool IsMovable(int id) =>
        id != ElectricIds.Air && id != ElectricIds.Piston && id != ElectricIds.PistonHead;

    public static bool IsBlocking(int id) => id != ElectricIds.Air;
}
