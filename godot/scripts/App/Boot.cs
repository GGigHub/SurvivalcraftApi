using Godot;

namespace SurvivalcraftGodot;

/// <summary>
/// 工程骨架入口。玩法不得写在这里。世界、方块、界面分别按 docs/modules 落到规定目录。
/// </summary>
public partial class Boot : Control
{
    public override void _Ready()
    {
        GD.Print("Survivalcraft Godot port skeleton. Gameplay is not implemented.");
    }
}
