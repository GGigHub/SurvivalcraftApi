using System.Collections.Generic;
using SurvivalcraftGodot.Simulation;

namespace SurvivalcraftGodot;

/// <summary>
/// 电路 / 活塞 / 爆炸纯逻辑验收。不依赖 Godot 场景。
/// 调用：ElectricChecks.Run()，空列表表示通过。
/// </summary>
public static class ElectricChecks
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckWireButtonLight(failures);
        CheckAndGate(failures);
        CheckOrGate(failures);
        CheckNotGate(failures);
        CheckPistonPushOne(failures);
        CheckKegExplosion(failures);
        CheckBlockIds(failures);
        return failures;
    }

    static void Check(List<string> failures, string name, bool ok)
    {
        if (!ok)
        {
            failures.Add(name);
        }
    }

    static void CheckBlockIds(List<string> failures)
    {
        Check(failures, "导线 Index=133", ElectricIds.Wire == 133);
        Check(failures, "与门 Index=137", ElectricIds.AndGate == 137);
        Check(failures, "灯 Index=139", ElectricIds.LightBulb == 139);
        Check(failures, "非门 Index=140", ElectricIds.NotGate == 140);
        Check(failures, "按钮 Index=142", ElectricIds.Button == 142);
        Check(failures, "或门 Index=143", ElectricIds.OrGate == 143);
        Check(failures, "小火药桶 Index=105", ElectricIds.SmallGunpowderKeg == 105);
        Check(failures, "活塞 Index=237", ElectricIds.Piston == 237);
    }

    static void CheckWireButtonLight(List<string> failures)
    {
        var grid = new SimGrid(5, 1, 1);
        grid.Set(0, 0, 0, ElectricIds.Button);
        grid.Set(1, 0, 0, ElectricIds.Wire);
        grid.Set(2, 0, 0, ElectricIds.LightBulb);
        CircuitNet net = CircuitNet.BuildFromGrid(grid);
        net.Step(2);
        var light = (LightBulbNode)net.Get(2, 0, 0)!;
        Check(failures, "按钮未按时灯灭", !light.IsOn);

        Check(failures, "能按下按钮", net.PressButton(0, 0, 0));
        net.Step(5);
        Check(failures, "按钮经导线点亮灯", light.IsOn);

        net.Step(20);
        Check(failures, "按钮松开后灯灭", !light.IsOn);
    }

    static void CheckAndGate(List<string> failures)
    {
        // 沿 +X：B1-Wire-And-Wire-Light；侧向 B2 进与门
        var grid = new SimGrid(5, 1, 2);
        grid.Set(0, 0, 0, ElectricIds.Button);
        grid.Set(1, 0, 0, ElectricIds.Wire);
        grid.Set(2, 0, 0, ElectricIds.AndGate);
        grid.Set(3, 0, 0, ElectricIds.Wire);
        grid.Set(4, 0, 0, ElectricIds.LightBulb);
        grid.Set(2, 0, 1, ElectricIds.Button);

        CircuitNet net = CircuitNet.BuildFromGrid(grid, gateFacing: 1);
        net.PressButton(0, 0, 0);
        net.Step(5);
        var light = (LightBulbNode)net.Get(4, 0, 0)!;
        Check(failures, "与门单侧高仍灭", !light.IsOn);

        // 重新搭一次，两键同按
        grid = new SimGrid(5, 1, 2);
        grid.Set(0, 0, 0, ElectricIds.Button);
        grid.Set(1, 0, 0, ElectricIds.Wire);
        grid.Set(2, 0, 0, ElectricIds.AndGate);
        grid.Set(3, 0, 0, ElectricIds.Wire);
        grid.Set(4, 0, 0, ElectricIds.LightBulb);
        grid.Set(2, 0, 1, ElectricIds.Button);
        net = CircuitNet.BuildFromGrid(grid, gateFacing: 1);
        net.PressButton(0, 0, 0);
        net.PressButton(2, 0, 1);
        net.Step(5);
        light = (LightBulbNode)net.Get(4, 0, 0)!;
        Check(failures, "与门两侧高则亮", light.IsOn);
    }

    static void CheckOrGate(List<string> failures)
    {
        var grid = new SimGrid(5, 1, 2);
        grid.Set(0, 0, 0, ElectricIds.Button);
        grid.Set(1, 0, 0, ElectricIds.Wire);
        grid.Set(2, 0, 0, ElectricIds.OrGate);
        grid.Set(3, 0, 0, ElectricIds.Wire);
        grid.Set(4, 0, 0, ElectricIds.LightBulb);
        grid.Set(2, 0, 1, ElectricIds.Button);

        CircuitNet net = CircuitNet.BuildFromGrid(grid, gateFacing: 1);
        net.PressButton(2, 0, 1);
        net.Step(5);
        var light = (LightBulbNode)net.Get(4, 0, 0)!;
        Check(failures, "或门单侧高则亮", light.IsOn);
    }

    static void CheckNotGate(List<string> failures)
    {
        var grid = new SimGrid(4, 1, 1);
        grid.Set(0, 0, 0, ElectricIds.Button);
        grid.Set(1, 0, 0, ElectricIds.NotGate);
        grid.Set(2, 0, 0, ElectricIds.Wire);
        grid.Set(3, 0, 0, ElectricIds.LightBulb);

        CircuitNet net = CircuitNet.BuildFromGrid(grid, gateFacing: 1);
        net.Step(5);
        var light = (LightBulbNode)net.Get(3, 0, 0)!;
        var not = (NotGateNode)net.Get(1, 0, 0)!;
        Check(failures, "非门无输入时输出高", ElectricNode.IsSignalHigh(not.GetOutputVoltage()));
        Check(failures, "非门默认点亮灯", light.IsOn);

        net.PressButton(0, 0, 0);
        net.Step(5);
        Check(failures, "非门输入高则输出低", !ElectricNode.IsSignalHigh(not.GetOutputVoltage()));
        Check(failures, "非门输入高时灯灭", !light.IsOn);
    }

    static void CheckPistonPushOne(List<string> failures)
    {
        var grid = new SimGrid(6, 1, 1);
        grid.Set(0, 0, 0, ElectricIds.Piston);
        grid.Set(1, 0, 0, ElectricIds.Dirt);
        grid.Set(2, 0, 0, ElectricIds.Air);

        bool ok = PistonRules.PushOne(grid, new GridPos(0, 0, 0), face: 1);
        Check(failures, "活塞推一格成功", ok);
        Check(failures, "活塞头占据前方", grid.Get(1, 0, 0) == ElectricIds.PistonHead);
        Check(failures, "泥土被推到下一格", grid.Get(2, 0, 0) == ElectricIds.Dirt);
        Check(failures, "活塞本体仍在", grid.Get(0, 0, 0) == ElectricIds.Piston);

        // 电路驱动：按钮 - 导线 - 活塞，电压满档伸出
        grid = new SimGrid(4, 1, 1);
        grid.Set(0, 0, 0, ElectricIds.Button);
        grid.Set(1, 0, 0, ElectricIds.Wire);
        grid.Set(2, 0, 0, ElectricIds.Piston);
        grid.Set(3, 0, 0, ElectricIds.Dirt);
        // 需要推入的空位：扩大格子
        grid = new SimGrid(5, 1, 1);
        grid.Set(0, 0, 0, ElectricIds.Button);
        grid.Set(1, 0, 0, ElectricIds.Wire);
        grid.Set(2, 0, 0, ElectricIds.Piston);
        grid.Set(3, 0, 0, ElectricIds.Dirt);
        grid.Set(4, 0, 0, ElectricIds.Air);

        CircuitNet net = CircuitNet.BuildFromGrid(grid, gateFacing: 1);
        net.PressButton(0, 0, 0);
        net.Step(5);
        var piston = (PistonNode)net.Get(2, 0, 0)!;
        Check(failures, "电路使活塞伸出长度>0", piston.LastLength > 0);
        Check(failures, "电路推动后泥土离开原位", grid.Get(3, 0, 0) != ElectricIds.Dirt);
    }

    static void CheckKegExplosion(List<string> failures)
    {
        var grid = new SimGrid(5, 3, 5);
        // 填满泥土，中心放小火药桶
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 5; z++)
                {
                    grid.Set(x, y, z, ElectricIds.Dirt);
                }
            }
        }

        var center = new GridPos(2, 1, 2);
        grid.Set(center, ElectricIds.SmallGunpowderKeg);
        // 导线从旁边进电
        grid.Set(1, 1, 2, ElectricIds.Wire);
        grid.Set(0, 1, 2, ElectricIds.Button);

        CircuitNet net = CircuitNet.BuildFromGrid(grid);
        net.PressButton(0, 1, 2);
        net.Step(5);

        Check(failures, "火药桶被电引爆后中心为空", grid.Get(center) == ElectricIds.Air);
        int cleared = ExplosionRules.CountClearedAround(grid, center, radius: 1);
        Check(failures, "爆炸挖开周围格子", cleared >= 3);

        // 直接爆炸 API（压力 60）
        var grid2 = new SimGrid(5, 3, 5);
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 5; z++)
                {
                    grid2.Set(x, y, z, ElectricIds.Dirt);
                }
            }
        }

        grid2.Set(2, 1, 2, ElectricIds.SmallGunpowderKeg);
        ExplosionRules.Explode(grid2, new GridPos(2, 1, 2), ElectricIds.KegExplosionPressure(ElectricIds.SmallGunpowderKeg));
        Check(failures, "直接爆炸清掉中心", grid2.Get(2, 1, 2) == ElectricIds.Air);
        Check(failures, "直接爆炸清掉邻格", grid2.Get(3, 1, 2) == ElectricIds.Air || grid2.Get(2, 1, 3) == ElectricIds.Air);
    }
}
