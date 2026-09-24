namespace SurvivalcraftGodot.Simulation;

/// <summary>原版方块 Index，对照 Survivalcraft/Block/*.cs 的 public static int Index。</summary>
public static class ElectricIds
{
    public const int Air = 0;
    public const int Dirt = 2;
    public const int Granite = 3;

    public const int Wire = 133;
    public const int AndGate = 137;
    public const int LightBulb = 139;
    public const int NotGate = 140;
    public const int Button = 142;
    public const int OrGate = 143;
    public const int SmallGunpowderKeg = 105;
    public const int MediumGunpowderKeg = 106;
    public const int LargeGunpowderKeg = 107;
    public const int Piston = 237;
    public const int PistonHead = 238;

    public static bool IsElectric(int id) =>
        id is Wire or AndGate or OrGate or NotGate or Button or LightBulb
            or SmallGunpowderKeg or MediumGunpowderKeg or LargeGunpowderKeg or Piston;

    public static bool IsGunpowderKeg(int id) =>
        id is SmallGunpowderKeg or MediumGunpowderKeg or LargeGunpowderKeg;

    public static float KegExplosionPressure(int id) => id switch
    {
        SmallGunpowderKeg => 60f,
        MediumGunpowderKeg => 360f,
        LargeGunpowderKeg => 2160f,
        _ => 0f
    };
}
