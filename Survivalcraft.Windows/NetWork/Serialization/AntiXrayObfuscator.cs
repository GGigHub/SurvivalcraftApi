using System;
using Game;

namespace Game.NetWork.Serialization
{
    public static class AntiXrayObfuscator
    {
        public static bool IsEnabled = false;
        public static int DisguiseContents(int contents)
        {
            if (contents == CoalOreBlock.Index || contents == CopperOreBlock.Index)
            {
                return GraniteBlock.Index;
            }
            if (contents == IronOreBlock.Index || contents == SulphurOreBlock.Index || contents == DiamondOreBlock.Index || contents == GermaniumOreBlock.Index)
            {
                return BasaltBlock.Index;
            }
            if (contents == SaltpeterOreBlock.Index)
            {
                return SandstoneBlock.Index;
            }
            return contents;
        }

        public static bool IsOre(int contents)
        {
            return contents == CoalOreBlock.Index
                || contents == CopperOreBlock.Index
                || contents == IronOreBlock.Index
                || contents == SulphurOreBlock.Index
                || contents == DiamondOreBlock.Index
                || contents == GermaniumOreBlock.Index
                || contents == SaltpeterOreBlock.Index;
        }

        public static int ObfuscateValue(int value)
        {
            if (!IsEnabled) { return value; }

            int contents = Terrain.ExtractContents(value);
            if (!IsOre(contents)) return value;

            int light = Terrain.ExtractLight(value);
            int data = Terrain.ExtractData(value);
            int disguised = DisguiseContents(contents);
            return Terrain.MakeBlockValue(disguised, light, data);
        }
    }
}
